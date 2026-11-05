using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    }

    private unsafe void OnFrameworkUpdate(IFramework _)
    {
        if (!config.Enabled) return;

        frameCounter++;
        if (frameCounter % ThrottleFrames != 0) return;

        try
        {
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
                            ProcessUldManager(&detail->JournalCanvasNode->UldManager);
                            if (detail->JournalCanvasNode->AtkResNode != null)
                            {
                                ProcessNodeTree(detail->JournalCanvasNode->AtkResNode);
                            }
                        }
                    }
                });
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
                            ProcessUldManager(&accept->JournalCanvas->UldManager);
                            if (accept->JournalCanvas->AtkResNode != null)
                            {
                                ProcessNodeTree(accept->JournalCanvas->AtkResNode);
                            }
                        }
                    }
                });
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
                            ProcessUldManager(&result->AtkComponentJournalCanvas268->UldManager);
                            if (result->AtkComponentJournalCanvas268->AtkResNode != null)
                            {
                                ProcessNodeTree(result->AtkComponentJournalCanvas268->AtkResNode);
                            }
                        }
                    }
                });
            }
        }
        catch (Exception ex)
        {
            log.Debug(ex, "任務說明欄節點掃描時發生非致命例外。");
        }
    }

    private unsafe delegate void AddonFieldProcessor(AtkUnitBase* addon);

    private unsafe void ProcessAddon(string addonName, AddonFieldProcessor? specificFieldProcessor = null)
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

        // 2. 深度遍歷 ULD 樹（自動深入 JournalCanvas 及所有文字元件）
        ProcessUldManager(&addon->UldManager);

        // 3. 遍歷整個節點樹鏈表 (RootNode -> ChildNode -> NextSiblingNode)
        if (addon->RootNode != null)
        {
            ProcessNodeTree(addon->RootNode);
        }
    }

    private unsafe void ProcessNodeTree(AtkResNode* node, int depth = 0)
    {
        if (node == null || depth > 10) return;

        if (node->Type == NodeType.Text)
        {
            TryProcessTextNode((AtkTextNode*)node);
        }
        else if (node->Type == NodeType.Component)
        {
            var compNode = (AtkComponentNode*)node;
            if (compNode->Component != null)
            {
                ProcessUldManager(&compNode->Component->UldManager, depth + 1);
            }
        }

        for (var child = node->ChildNode; child != null; child = child->NextSiblingNode)
        {
            ProcessNodeTree(child, depth + 1);
        }
    }

    private unsafe void ProcessUldManager(AtkUldManager* uldManager, int depth = 0)
    {
        if (uldManager == null || uldManager->NodeList == null || depth > 8) return;

        for (var i = 0; i < uldManager->NodeListCount; i++)
        {
            var node = uldManager->NodeList[i];
            if (node == null) continue;

            if (node->Type == NodeType.Text)
            {
                var textNode = (AtkTextNode*)node;
                TryProcessTextNode(textNode);
            }
            else if (node->Type == NodeType.Component)
            {
                var compNode = (AtkComponentNode*)node;
                if (compNode->Component != null)
                {
                    ProcessUldManager(&compNode->Component->UldManager, depth + 1);
                }
            }

            if (node->ChildNode != null)
            {
                ProcessNodeTree(node->ChildNode, depth + 1);
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

        var trimmed = currentText.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return;

        // 檢查是否已經是我們替換過的譯文
        lock (translatedSet)
        {
            if (translatedSet.Contains(trimmed)) return;
        }

        // 過濾系統按鈕、純數字、等級等不需翻譯的標籤
        if (IsIgnoredOrSystemLabel(trimmed)) return;

        // 確認含有可翻譯字元 (日文假名、漢字、或英文字詞)
        if (!ContainsTranslatableCharacters(trimmed)) return;

        // 若快取中已有對應譯文，直接在主執行緒安全執行 SetText
        if (translationCache.TryGetValue(trimmed, out var cachedTranslation))
        {
            if (trimmed != cachedTranslation)
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
                }
                catch (Exception ex)
                {
                    log.Warning(ex, $"調用 SetText 替換任務說明節點文字失敗: {cachedTranslation}");
                }
            }
            return;
        }

        // 若無快取且尚未送翻，啟動背景非同步翻譯
        if (pendingTranslations.TryAdd(trimmed, true))
        {
            RequestTranslationAsync(trimmed);
        }
    }

    private void RequestTranslationAsync(string trimmed)
    {
        Task.Run(async () =>
        {
            try
            {
                var (_, translated) = await translator.TranslateAsync(string.Empty, trimmed);

                // 避免將錯誤代碼（如 [Gemini 錯誤]）寫入遊戲原生介面
                if (!string.IsNullOrWhiteSpace(translated) && !translated.StartsWith("["))
                {
                    // 若啟用缺字轉折映射，自動將中文字元轉換為遊戲字型支援之字元
                    string finalResult = config.QuestEnableGlyphFolding
                        ? GlyphFolder.Fold(translated, config.CustomGlyphEntries)
                        : translated;

                    translationCache[trimmed] = finalResult;
                    lock (translatedSet)
                    {
                        translatedSet.Add(finalResult);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Warning(ex, $"任務說明翻譯失敗: {trimmed}");
            }
            finally
            {
                pendingTranslations.TryRemove(trimmed, out _);
            }
        });
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
