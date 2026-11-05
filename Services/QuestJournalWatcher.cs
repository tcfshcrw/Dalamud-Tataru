using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
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
    private readonly HashSet<string> translatedSet = new();

    // 除錯與即時診斷佇列
    public readonly ConcurrentQueue<string> RecentDetectedTexts = new();
    private const int MaxLogQueue = 25;

    private int frameCounter = 0;
    private const int ThrottleFrames = 6;

    private static readonly HashSet<string> IgnoredUiLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        // 日文原生 UI 按鈕與標籤
        "受領する", "破棄", "戻る", "辞退", "辭退", "コンプリート", "マップ", "決定", "閉じる", "再挑戦",
        "確認", "はい", "いいえ", "ギル", "EXP", "経験値", "經驗值", "報酬", "選択報酬", "オプション", "受注条件",
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
        lock (translatedSet)
        {
            translatedSet.Clear();
        }
        while (RecentDetectedTexts.TryDequeue(out _)) { }
    }

    private void AddDiagnosticLog(string entry)
    {
        RecentDetectedTexts.Enqueue(entry);
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
                            ProcessUldManager(&detail->JournalCanvasNode->UldManager, visited);
                            if (detail->JournalCanvasNode->AtkResNode != null)
                            {
                                ProcessNodeTree(detail->JournalCanvasNode->AtkResNode, visited);
                            }
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
                            ProcessUldManager(&accept->JournalCanvas->UldManager, visited);
                            if (accept->JournalCanvas->AtkResNode != null)
                            {
                                ProcessNodeTree(accept->JournalCanvas->AtkResNode, visited);
                            }
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
                            ProcessUldManager(&result->AtkComponentJournalCanvas268->UldManager, visited);
                            if (result->AtkComponentJournalCanvas268->AtkResNode != null)
                            {
                                ProcessNodeTree(result->AtkComponentJournalCanvas268->AtkResNode, visited);
                            }
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
        if (node == null || depth > 16) return;
        if (!visited.Add((nint)node)) return;

        if (node->Type == NodeType.Text)
        {
            TryProcessTextNode((AtkTextNode*)node);
        }
        else if (node->Type == NodeType.Component)
        {
            var compNode = (AtkComponentNode*)node;
            if (compNode->Component != null)
            {
                ProcessUldManager(&compNode->Component->UldManager, visited, depth + 1);
                if (compNode->Component->AtkResNode != null)
                {
                    ProcessNodeTree(compNode->Component->AtkResNode, visited, depth + 1);
                }

                // 檢查 ListItemRenderer 的專有文字節點
                try
                {
                    var renderer = (AtkComponentListItemRenderer*)compNode->Component;
                    if (renderer != null && renderer->ButtonTextNode != null)
                    {
                        TryProcessTextNode(renderer->ButtonTextNode);
                    }
                }
                catch
                {
                    // 忽略非 ListItemRenderer 型別
                }
            }
        }

        // 雙向完整覆蓋子兄弟鏈表
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
        if (uldManager == null || depth > 14) return;
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

        // 清理二進位控制字符，取得乾淨純文字
        var cleanText = CleanSeStringText(currentText);
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

        // 若無快取且尚未送翻，啟動背景非同步翻譯
        if (pendingTranslations.TryAdd(cleanText, true))
        {
            AddDiagnosticLog($"[翻譯中] {cleanText.Substring(0, Math.Min(30, cleanText.Length))}...");
            RequestTranslationAsync(cleanText);
        }
    }

    private void RequestTranslationAsync(string cleanText)
    {
        Task.Run(async () =>
        {
            try
            {
                var (_, rawTranslated) = await translator.TranslateAsync(string.Empty, cleanText);

                // 檢查是否為錯誤代碼（避免寫入介面）
                if (IsTranslationError(rawTranslated))
                {
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
                log.Warning(ex, $"任務說明翻譯失敗: {cleanText}");
                AddDiagnosticLog($"[異常] {ex.Message}");
            }
            finally
            {
                pendingTranslations.TryRemove(cleanText, out _);
            }
        });
    }

    private static string CleanSeStringText(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (c == '\n' || c == '\r' || c == '\t')
            {
                sb.Append(c);
                continue;
            }
            // 排除二進位控制字符 (0x00 - 0x1F) 以及私有不可見控制字元
            if (c < 0x20 || c == 0x7F)
            {
                continue;
            }
            sb.Append(c);
        }

        return sb.ToString().Trim();
    }

    private static bool IsQuestRecapText(string text)
    {
        return text.StartsWith("前回までの冒険録", StringComparison.OrdinalIgnoreCase)
            || text.Contains("前回までの冒険録")
            || text.StartsWith("The story thus far", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTranslationError(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;

        return text.StartsWith("[Gemini") ||
               text.StartsWith("[模型") ||
               text.StartsWith("【請先於設定中輸入") ||
               text.StartsWith("[翻譯未回傳結果]") ||
               text.StartsWith("[翻譯請求逾時]") ||
               text.StartsWith("[翻譯異常") ||
               text.StartsWith("[LM Studio") ||
               text.StartsWith("[有道異常") ||
               text.StartsWith("[有道翻譯錯誤") ||
               text.StartsWith("[有道服務") ||
               text.StartsWith("[Google 錯誤") ||
               text.StartsWith("[Google 異常") ||
               text.StartsWith("[所有翻譯管道皆失敗]") ||
               text.StartsWith("[LM Studio 與免費備用防線皆翻譯失敗]");
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

    private static bool IsIgnoredOrSystemLabel(string text)
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

    private static bool ContainsTranslatableCharacters(string text)
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
    }
}
