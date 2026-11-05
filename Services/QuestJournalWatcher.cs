using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DalamudTataru.Services;

public class QuestJournalWatcher : IDisposable
{
    private readonly IFramework framework;
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly GeminiTranslator translator;

    private readonly ConcurrentDictionary<string, string> translationCache = new();
    private readonly ConcurrentDictionary<string, bool> pendingTranslations = new();
    private readonly ConcurrentDictionary<string, DateTime> failedCooldowns = new();
    private readonly HashSet<string> translatedSet = new();
    private readonly SemaphoreSlim translationSemaphore = new(2, 2);

    // 除錯與即時診斷佇列
    public readonly ConcurrentQueue<string> RecentDetectedTexts = new();
    private const int MaxLogQueue = 40;

    private int frameCounter = 0;
    private const int ThrottleFrames = 6;

    private static readonly HashSet<string> IgnoredUiLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        // 日文原生 UI 按鈕與標籤
        "受領する", "受注", "破棄", "戻る", "辞退", "辭退", "コンプリート", "マップ", "マップ表示", "決定", "閉じる", "再挑戦",
        "確認", "はい", "いいえ", "キャンセル", "ギル", "EXP", "経験値", "經驗值", "報酬", "選択報酬", "オプション", "受注条件",
        // 英文原生 UI 按鈕與標籤
        "Accept", "Decline", "Abandon", "Complete", "Map", "OK", "Cancel", "Close", "Retry",
        "Yes", "No", "Gil", "Reward", "Rewards", "Optional", "Requirements"
    };

    public QuestJournalWatcher(IFramework framework, IGameGui gameGui, IPluginLog log, Configuration config, GeminiTranslator translator)
    {
        this.framework = framework;
        this.gameGui = gameGui;
        this.log = log;
        this.config = config;
        this.translator = translator;

        this.framework.Update += OnFrameworkUpdate;
    }

    public int CacheCount => translationCache.Count;

    public void ClearCache()
    {
        translationCache.Clear();
        pendingTranslations.Clear();
        failedCooldowns.Clear();
        lock (translatedSet)
        {
            translatedSet.Clear();
        }
        while (RecentDetectedTexts.TryDequeue(out _)) { }
    }

    public string GetNodeStatus(string cleanText, string rawText)
    {
        if (string.IsNullOrWhiteSpace(cleanText)) return "Empty";
        lock (translatedSet)
        {
            if (translatedSet.Contains(cleanText) || translatedSet.Contains(rawText))
            {
                return "AlreadyReplaced (InTranslatedSet)";
            }
        }
        if (translationCache.TryGetValue(cleanText, out var cached))
        {
            return $"Cached -> '{cached.Substring(0, Math.Min(25, cached.Length))}'";
        }
        if (pendingTranslations.ContainsKey(cleanText))
        {
            return "Translating (Pending)";
        }
        if (failedCooldowns.TryGetValue(cleanText, out var retryTime) && DateTime.UtcNow < retryTime)
        {
            return $"RetryCooldown ({(retryTime - DateTime.UtcNow).TotalSeconds:F1}s)";
        }
        if (!config.TranslateJournalRecap && IsQuestRecapText(cleanText))
        {
            return "Skipped (RecapDisabled)";
        }
        if (IsIgnoredOrSystemLabel(cleanText))
        {
            return "Skipped (IgnoredSystemLabel)";
        }
        if (!ContainsTranslatableCharacters(cleanText))
        {
            return "Skipped (NoTranslatableChars)";
        }
        return "Unprocessed";
    }

    private void AddDiagnosticLog(string entry)
    {
        RecentDetectedTexts.Enqueue($"[{DateTime.Now:HH:mm:ss}] {entry}");
        while (RecentDetectedTexts.Count > MaxLogQueue)
        {
            RecentDetectedTexts.TryDequeue(out _);
        }
    }

    private unsafe void OnFrameworkUpdate(IFramework _)
    {
        if (!config.Enabled) return;

        frameCounter++;
        if (frameCounter % ThrottleFrames != 0) return;

        try
        {
            var visited = new HashSet<nint>();

            // 1. 任務詳細說明欄 (JournalDetail)
            if (config.TranslateJournalDetail)
            {
                ProcessAddon("JournalDetail", addon =>
                {
                    var detail = (AddonJournalDetail*)addon;
                    if (detail != null)
                    {
                        TryProcessTextNode(detail->DutyNameTextNode);
                        TryProcessTextNode(detail->DutyLevelTextNode);
                        TryProcessTextNode(detail->RewardsReceivedTextNode);
                        TryProcessTextNode(detail->AtkTextNode2A8);
                        TryProcessTextNode(detail->RequirementsNotMetLabelTextNode);
                        TryProcessTextNode(detail->RequirementsNotMetTextNode);
                        TryProcessTextNode(detail->AtkTextNode2C0);

                        if (detail->JournalCanvasNode != null)
                        {
                            ProcessJournalCanvas(detail->JournalCanvasNode, visited);
                        }
                    }
                }, visited);
            }

            // 2. 接取任務確認視窗 (JournalAccept)
            if (config.TranslateJournalAccept)
            {
                ProcessAddon("JournalAccept", addon =>
                {
                    var accept = (AddonJournalAccept*)addon;
                    if (accept != null)
                    {
                        TryProcessTextNode(accept->QuestTitleText);
                        TryProcessTextNode(accept->JournalCanvasText);

                        if (accept->JournalCanvas != null)
                        {
                            ProcessJournalCanvas(accept->JournalCanvas, visited);
                        }
                    }
                }, visited);
            }

            // 3. 回報完成任務視窗 (JournalResult)
            if (config.TranslateJournalResult)
            {
                ProcessAddon("JournalResult", addon =>
                {
                    var result = (AddonJournalResult*)addon;
                    if (result != null)
                    {
                        TryProcessTextNode(result->AtkTextNode250);
                        TryProcessTextNode(result->AtkTextNode258);

                        if (result->AtkComponentJournalCanvas268 != null)
                        {
                            ProcessJournalCanvas(result->AtkComponentJournalCanvas268, visited);
                        }
                    }
                }, visited);
            }
        }
        catch (Exception ex)
        {
            log.Debug(ex, "任務說明欄節點掃描時發生非致命例外。");
        }
    }

    private unsafe void ProcessJournalCanvas(AtkComponentJournalCanvas* canvas, HashSet<nint> visited)
    {
        if (canvas == null) return;

        ProcessUldManager(&canvas->UldManager, visited);
        if (canvas->AtkResNode != null)
        {
            ProcessNodeTree(canvas->AtkResNode, visited);
        }
        if (canvas->OwnerNode != null)
        {
            ProcessNodeTree(&canvas->OwnerNode->AtkResNode, visited);
        }
    }

    private unsafe delegate void AddonFieldProcessor(AtkUnitBase* addon);

    private unsafe void ProcessAddon(string addonName, AddonFieldProcessor? specificFieldProcessor, HashSet<nint> visited)
    {
        var addonPtr = gameGui.GetAddonByName(addonName);
        if (addonPtr.IsNull) return;

        var addon = (AtkUnitBase*)addonPtr.Address;
        if (addon == null || !addon->IsVisible || !addon->IsReady) return;

        // 1. 處理專有已知欄位
        try
        {
            specificFieldProcessor?.Invoke(addon);
        }
        catch
        {
            // 忽略個別結構欄位異常
        }

        // 2. 深度遍歷 ULD 樹（包括 RootNode、NodeList）
        ProcessUldManager(&addon->UldManager, visited);

        // 3. 遍歷整個節點樹
        if (addon->RootNode != null)
        {
            ProcessNodeTree(addon->RootNode, visited);
        }
    }

    private unsafe void ProcessNodeTree(AtkResNode* node, HashSet<nint> visited, int depth = 0)
    {
        if (node == null || depth > 20) return;
        if (!visited.Add((nint)node)) return;

        var nodeType = node->GetNodeType();
        if (nodeType == NodeType.Text || node->Type == NodeType.Text)
        {
            var textNode = node->GetAsAtkTextNode();
            if (textNode == null) textNode = (AtkTextNode*)node;
            TryProcessTextNode(textNode);
        }
        else if ((int)node->Type >= 1000 || nodeType == NodeType.Component)
        {
            var compNode = node->GetAsAtkComponentNode();
            if (compNode == null) compNode = (AtkComponentNode*)node;

            if (compNode != null && compNode->Component != null)
            {
                ProcessUldManager(&compNode->Component->UldManager, visited, depth + 1);
                if (compNode->Component->AtkResNode != null)
                {
                    ProcessNodeTree(compNode->Component->AtkResNode, visited, depth + 1);
                }

                try
                {
                    var compType = compNode->Component->GetComponentType();
                    if (compType == ComponentType.ListItemRenderer)
                    {
                        var renderer = (AtkComponentListItemRenderer*)compNode->Component;
                        if (renderer != null && renderer->ButtonTextNode != null)
                        {
                            TryProcessTextNode(renderer->ButtonTextNode);
                        }
                    }
                    else if (compType == ComponentType.Button)
                    {
                        var btn = (AtkComponentButton*)compNode->Component;
                        if (btn != null && btn->ButtonTextNode != null)
                        {
                            TryProcessTextNode(btn->ButtonTextNode);
                        }
                    }
                }
                catch
                {
                    // 忽略非預期元件轉型異常
                }
            }
        }

        // 1. 雙向走訪當前節點自身所在層級的兄弟鏈表 (確保 RootNode 或 AtkResNode 的同層兄弟節點不漏掃)
        for (var next = node->NextSiblingNode; next != null; next = next->NextSiblingNode)
        {
            ProcessNodeTree(next, visited, depth);
        }
        for (var prev = node->PrevSiblingNode; prev != null; prev = prev->PrevSiblingNode)
        {
            ProcessNodeTree(prev, visited, depth);
        }

        // 2. 雙向完整覆蓋子節點鏈表
        var child = node->ChildNode;
        if (child != null)
        {
            for (var curr = child; curr != null; curr = curr->NextSiblingNode)
            {
                ProcessNodeTree(curr, visited, depth + 1);
            }
            for (var curr = child->PrevSiblingNode; curr != null; curr = curr->PrevSiblingNode)
            {
                ProcessNodeTree(curr, visited, depth + 1);
            }
        }
    }

    private unsafe void ProcessUldManager(AtkUldManager* uldManager, HashSet<nint> visited, int depth = 0)
    {
        if (uldManager == null || depth > 18) return;
        if (!visited.Add((nint)uldManager)) return;

        // 1. 遍歷 RootNode
        if (uldManager->RootNode != null)
        {
            ProcessNodeTree(uldManager->RootNode, visited, depth + 1);
        }

        // 2. 遍歷 NodeList
        if (uldManager->NodeList != null)
        {
            for (var i = 0; i < uldManager->NodeListCount; i++)
            {
                var node = uldManager->NodeList[i];
                if (node == null) continue;
                ProcessNodeTree(node, visited, depth + 1);
            }
        }
    }

    private unsafe void TryProcessTextNode(AtkTextNode* textNode)
    {
        if (textNode == null) return;

        string currentText;
        try
        {
            currentText = textNode->NodeText.ToString();
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(currentText)) return;

        // 解析 SeString 並清理二進位控制字符，取得乾淨純文字
        var cleanText = ExtractCleanText(textNode, currentText);
        if (string.IsNullOrWhiteSpace(cleanText)) return;

        // 檢查是否已經是我們替換過的譯文
        lock (translatedSet)
        {
            if (translatedSet.Contains(cleanText) || translatedSet.Contains(currentText)) return;
        }

        // 檢查是否為「總結歷程/冒險錄」且用戶未開啟該選項
        if (!config.TranslateJournalRecap && IsQuestRecapText(cleanText))
        {
            return;
        }

        // 過濾系統按鈕、純數字、等級等不需翻譯的標籤
        if (IsIgnoredOrSystemLabel(cleanText)) return;

        // 確認含有可翻譯字元 (日文假名、漢字、或英文字詞)
        if (!ContainsTranslatableCharacters(cleanText)) return;

        // 若快取中已有對應譯文，直接在主執行緒安全執行 SetText
        if (translationCache.TryGetValue(cleanText, out var cachedTranslation))
        {
            if (currentText != cachedTranslation && cleanText != cachedTranslation)
            {
                try
                {
                    textNode->SetText(cachedTranslation);
                    try
                    {
                        textNode->ResizeNodeForCurrentText();
                    }
                    catch
                    {
                        // 某些固定排版節點可能不支援或無須手動重設尺寸
                    }

                    lock (translatedSet)
                    {
                        translatedSet.Add(cachedTranslation);
                    }
                    AddDiagnosticLog($"[已套用] {cleanText.Substring(0, Math.Min(20, cleanText.Length))}... -> {cachedTranslation.Substring(0, Math.Min(20, cachedTranslation.Length))}...");
                }
                catch (Exception ex)
                {
                    log.Warning(ex, $"調用 SetText 替換任務說明節點文字失敗: {cachedTranslation}");
                }
            }
            return;
        }

        // 若剛發生過翻譯錯誤，等待冷卻時間後再重試，避免每 6 幀無限洗版請求
        if (failedCooldowns.TryGetValue(cleanText, out var retryAfter) && DateTime.UtcNow < retryAfter)
        {
            return;
        }

        // 若無快取且尚未送翻，啟動背景非同步翻譯
        if (pendingTranslations.TryAdd(cleanText, true))
        {
            AddDiagnosticLog($"[送出翻譯] {cleanText.Substring(0, Math.Min(30, cleanText.Length))}...");
            RequestTranslationAsync(cleanText);
        }
    }

    private void RequestTranslationAsync(string cleanText)
    {
        Task.Run(async () =>
        {
            bool acquired = false;
            try
            {
                await translationSemaphore.WaitAsync();
                acquired = true;

                // 進入排程後再次確認是否已在快取中
                if (translationCache.ContainsKey(cleanText))
                {
                    return;
                }

                var (_, rawTranslated) = await translator.TranslateAsync(string.Empty, cleanText);

                // 檢查是否為錯誤代碼（避免寫入介面，並設定 5 秒重試冷卻）
                if (IsTranslationError(rawTranslated))
                {
                    failedCooldowns[cleanText] = DateTime.UtcNow.AddSeconds(5);
                    AddDiagnosticLog($"[翻譯錯誤] {cleanText.Substring(0, Math.Min(15, cleanText.Length))}: {rawTranslated}");
                    return;
                }

                // 剝離免費備用防線的前綴標籤，以純譯文形式替換原生 UI
                string translated = CleanFallbackPrefix(rawTranslated);

                if (!string.IsNullOrWhiteSpace(translated))
                {
                    // 若啟用缺字轉折映射，自動將中文字元轉換為遊戲字型支援之字元
                    string finalResult = config.QuestEnableGlyphFolding
                        ? GlyphFolder.Fold(translated, config.CustomGlyphEntries)
                        : translated;

                    failedCooldowns.TryRemove(cleanText, out _);
                    translationCache[cleanText] = finalResult;
                    lock (translatedSet)
                    {
                        translatedSet.Add(finalResult);
                    }
                    AddDiagnosticLog($"[翻譯完成] {cleanText.Substring(0, Math.Min(15, cleanText.Length))} -> {finalResult.Substring(0, Math.Min(15, finalResult.Length))}");
                }
            }
            catch (Exception ex)
            {
                failedCooldowns[cleanText] = DateTime.UtcNow.AddSeconds(5);
                log.Warning(ex, $"任務說明翻譯失敗: {cleanText}");
                AddDiagnosticLog($"[異常] {ex.Message}");
            }
            finally
            {
                if (acquired)
                {
                    translationSemaphore.Release();
                }
                pendingTranslations.TryRemove(cleanText, out _);
            }
        });
    }

    internal static unsafe string ExtractCleanText(AtkTextNode* textNode, string fallbackRaw)
    {
        if (textNode != null)
        {
            try
            {
                var span = textNode->NodeText.AsSpan();
                if (span.Length > 0)
                {
                    var parsed = SeString.Parse(span);
                    var textVal = parsed.TextValue;
                    if (!string.IsNullOrWhiteSpace(textVal))
                    {
                        return CleanSeStringText(textVal);
                    }
                }
            }
            catch
            {
                // 若 SeString 解析失敗則退回字串清理
            }
        }
        return CleanSeStringText(fallbackRaw);
    }

    internal static string CleanSeStringText(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var sb = new StringBuilder(raw.Length);
        bool inPayload = false;

        foreach (var c in raw)
        {
            if (c == 0x02)
            {
                inPayload = true;
                continue;
            }
            if (c == 0x03)
            {
                inPayload = false;
                continue;
            }
            if (inPayload)
            {
                continue;
            }

            if (c == '\n' || c == '\r' || c == '\t')
            {
                sb.Append(c);
                continue;
            }
            // 排除二進位控制字符 (0x00 - 0x1F) 以及私有不可見圖示字元/替換字元
            if (c < 0x20 || c == 0x7F || c == 0xFFFD || (c >= 0xE000 && c <= 0xF8FF))
            {
                continue;
            }
            sb.Append(c);
        }

        return sb.ToString().Trim();
    }

    internal static bool IsQuestRecapText(string text)
    {
        return text.StartsWith("前回までの冒険録", StringComparison.OrdinalIgnoreCase)
            || text.Contains("前回までの冒険録")
            || text.StartsWith("The story thus far", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTranslationError(string? text)
    {
        return GeminiTranslator.IsAnyTranslationError(text);
    }

    private static string CleanFallbackPrefix(string text)
    {
        if (text.StartsWith("[有道備用] "))
        {
            return text.Substring("[有道備用] ".Length);
        }
        if (text.StartsWith("[谷歌備用] "))
        {
            return text.Substring("[谷歌備用] ".Length);
        }
        return text;
    }

    internal static bool IsIgnoredOrSystemLabel(string text)
    {
        if (IgnoredUiLabels.Contains(text)) return true;

        // 純數字或帶逗號千分位數字 (例如: "1", "1,000", "50")
        if (int.TryParse(text.Replace(",", "").Replace(".", ""), out _)) return true;

        // 等級標籤 (例如: "Lv 50", "Lv.90", "LV70")
        if (text.StartsWith("Lv", StringComparison.OrdinalIgnoreCase)) return true;

        // 進度數值 (例如: "0/1", "12/20")
        if (text.Contains('/') && text.Length <= 7) return true;

        // 極短符號字串
        if (text.Length <= 1) return true;

        return false;
    }

    internal static bool ContainsTranslatableCharacters(string text)
    {
        foreach (var c in text)
        {
            if ((c >= 0x3040 && c <= 0x309F) || // 平假名
                (c >= 0x30A0 && c <= 0x30FF) || // 片假名
                (c >= 0x4E00 && c <= 0x9FFF))   // CJK 漢字
            {
                return true;
            }
        }

        // 支援純英文句（至少包含 4 個拉丁字母）
        int latinCount = 0;
        foreach (var c in text)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
            {
                latinCount++;
                if (latinCount >= 4) return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        this.framework.Update -= OnFrameworkUpdate;
        ClearCache();
        translationSemaphore.Dispose();
    }
}
