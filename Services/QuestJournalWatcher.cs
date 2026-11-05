using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using DalamudTataru.Models;
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

    // 滑鼠懸浮追蹤狀態
    public QuestHoverInfo? CurrentHover { get; private set; }
    private string? lastHoverCandidateClean = null;
    private DateTime hoverStartTime = DateTime.MinValue;
    private const int HoverDebounceMs = 80;

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
        CurrentHover = null;
        lastHoverCandidateClean = null;
        while (RecentDetectedTexts.TryDequeue(out _)) { }
    }

    public string GetNodeStatus(string cleanText, string rawText)
    {
        if (string.IsNullOrWhiteSpace(cleanText)) return "Empty";
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
        return "ReadyForHover";
    }

    private void AddDiagnosticLog(string entry)
    {
        RecentDetectedTexts.Enqueue($"[{DateTime.Now:HH:mm:ss}] {entry}");
        while (RecentDetectedTexts.Count > MaxLogQueue)
        {
            RecentDetectedTexts.TryDequeue(out _);
        }
    }

    /// <summary>
    /// 由 ImGui UI 繪製循環每幀呼叫，精準偵測滑鼠是否懸浮在任務內容文字上
    /// </summary>
    public unsafe void UpdateHover(Vector2 mousePos)
    {
        if (!config.Enabled || !config.QuestHoverTranslate)
        {
            CurrentHover = null;
            lastHoverCandidateClean = null;
            return;
        }

        try
        {
            AtkTextNode* bestCandidate = null;
            FFXIVClientStructs.FFXIV.Common.Math.Bounds bestBounds = default;
            string bestClean = string.Empty;
            string bestAddon = string.Empty;
            float bestArea = float.MaxValue;

            var visited = new HashSet<nint>();

            // 1. 任務詳細說明欄 (JournalDetail)
            if (config.TranslateJournalDetail)
            {
                CheckAddonHover("JournalDetail", mousePos, (addon, titleNode, titleText, canvasBounds) =>
                {
                    var detail = (AddonJournalDetail*)addon;
                    if (detail != null)
                    {
                        // 記錄標題文字節點 (嚴格跳過任務名稱，不翻譯任務名稱)
                        if (detail->DutyNameTextNode != null)
                        {
                            titleNode = (nint)detail->DutyNameTextNode;
                            titleText = ExtractCleanText(detail->DutyNameTextNode, "");
                        }

                        // 取得 JournalCanvas 畫布螢幕邊界
                        if (detail->JournalCanvasNode != null && detail->JournalCanvasNode->AtkResNode != null)
                        {
                            FFXIVClientStructs.FFXIV.Common.Math.Bounds cb = default;
                            detail->JournalCanvasNode->AtkResNode->GetBounds(&cb);
                            if (cb.Width > 10 && cb.Height > 10)
                            {
                                canvasBounds = cb;
                            }
                        }

                        // 掃描畫布內容
                        if (detail->JournalCanvasNode != null)
                        {
                            ScanCanvasHover(detail->JournalCanvasNode, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, "JournalDetail");
                        }

                        // 掃描其他內容文字節點
                        CheckCandidateHover(detail->AtkTextNode2A8, titleNode, titleText, canvasBounds, mousePos, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, "JournalDetail");
                        CheckCandidateHover(detail->RequirementsNotMetTextNode, titleNode, titleText, canvasBounds, mousePos, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, "JournalDetail");
                        CheckCandidateHover(detail->AtkTextNode2C0, titleNode, titleText, canvasBounds, mousePos, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, "JournalDetail");

                        // 深度掃描 ULD 樹中的其他內容
                        ScanUldHover(&detail->AtkUnitBase.UldManager, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, "JournalDetail");
                    }
                }, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon);
            }

            // 2. 接取任務確認視窗 (JournalAccept)
            if (config.TranslateJournalAccept && bestCandidate == null)
            {
                CheckAddonHover("JournalAccept", mousePos, (addon, titleNode, titleText, canvasBounds) =>
                {
                    var accept = (AddonJournalAccept*)addon;
                    if (accept != null)
                    {
                        // 記錄標題文字節點 (不翻譯任務名稱)
                        if (accept->QuestTitleText != null)
                        {
                            titleNode = (nint)accept->QuestTitleText;
                            titleText = ExtractCleanText(accept->QuestTitleText, "");
                        }

                        // 取得 JournalCanvas 畫布邊界
                        if (accept->JournalCanvas != null && accept->JournalCanvas->AtkResNode != null)
                        {
                            FFXIVClientStructs.FFXIV.Common.Math.Bounds cb = default;
                            accept->JournalCanvas->AtkResNode->GetBounds(&cb);
                            if (cb.Width > 10 && cb.Height > 10)
                            {
                                canvasBounds = cb;
                            }
                        }

                        CheckCandidateHover(accept->JournalCanvasText, titleNode, titleText, canvasBounds, mousePos, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, "JournalAccept");

                        if (accept->JournalCanvas != null)
                        {
                            ScanCanvasHover(accept->JournalCanvas, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, "JournalAccept");
                        }

                        ScanUldHover(&accept->AtkUnitBase.UldManager, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, "JournalAccept");
                    }
                }, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon);
            }

            // 3. 回報完成任務視窗 (JournalResult)
            if (config.TranslateJournalResult && bestCandidate == null)
            {
                CheckAddonHover("JournalResult", mousePos, (addon, titleNode, titleText, canvasBounds) =>
                {
                    var result = (AddonJournalResult*)addon;
                    if (result != null)
                    {
                        // AtkTextNode250 通常為任務名稱 (不翻譯任務名稱)
                        if (result->AtkTextNode250 != null)
                        {
                            titleNode = (nint)result->AtkTextNode250;
                            titleText = ExtractCleanText(result->AtkTextNode250, "");
                        }

                        if (result->AtkComponentJournalCanvas268 != null && result->AtkComponentJournalCanvas268->AtkResNode != null)
                        {
                            FFXIVClientStructs.FFXIV.Common.Math.Bounds cb = default;
                            result->AtkComponentJournalCanvas268->AtkResNode->GetBounds(&cb);
                            if (cb.Width > 10 && cb.Height > 10)
                            {
                                canvasBounds = cb;
                            }
                        }

                        CheckCandidateHover(result->AtkTextNode258, titleNode, titleText, canvasBounds, mousePos, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, "JournalResult");

                        if (result->AtkComponentJournalCanvas268 != null)
                        {
                            ScanCanvasHover(result->AtkComponentJournalCanvas268, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, "JournalResult");
                        }

                        ScanUldHover(&result->AtkUnitBase.UldManager, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, "JournalResult");
                    }
                }, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon);
            }

            // 處理最佳候選懸浮文字
            if (bestCandidate != null && !string.IsNullOrWhiteSpace(bestClean))
            {
                if (lastHoverCandidateClean != bestClean)
                {
                    lastHoverCandidateClean = bestClean;
                    hoverStartTime = DateTime.UtcNow;
                }

                // 防手抖抖動防護：懸停達指定毫秒後啟動非同步翻譯
                bool debouncePassed = (DateTime.UtcNow - hoverStartTime).TotalMilliseconds >= HoverDebounceMs;
                if (debouncePassed)
                {
                    if (!translationCache.ContainsKey(bestClean) &&
                        !pendingTranslations.ContainsKey(bestClean) &&
                        (!failedCooldowns.TryGetValue(bestClean, out var retryAfter) || DateTime.UtcNow >= retryAfter))
                    {
                        if (pendingTranslations.TryAdd(bestClean, true))
                        {
                            AddDiagnosticLog($"[懸停送翻] {bestClean.Substring(0, Math.Min(30, bestClean.Length))}...");
                            RequestTranslationAsync(bestClean);
                        }
                    }
                }

                translationCache.TryGetValue(bestClean, out var cachedTranslation);
                bool isPending = pendingTranslations.ContainsKey(bestClean);

                CurrentHover = new QuestHoverInfo
                {
                    CleanText = bestClean,
                    TranslatedText = cachedTranslation,
                    IsPending = isPending,
                    MousePos = mousePos,
                    NodePos = new Vector2(Math.Min(bestBounds.Pos1.X, bestBounds.Pos2.X), Math.Min(bestBounds.Pos1.Y, bestBounds.Pos2.Y)),
                    NodeSize = new Vector2(Math.Abs(bestBounds.Pos2.X - bestBounds.Pos1.X), Math.Abs(bestBounds.Pos2.Y - bestBounds.Pos1.Y)),
                    NodePointer = (nint)bestCandidate,
                    AddonName = bestAddon
                };
            }
            else
            {
                lastHoverCandidateClean = null;
                CurrentHover = null;
            }
        }
        catch (Exception ex)
        {
            log.Debug(ex, "任務懸浮掃描時發生非致命例外。");
            CurrentHover = null;
        }
    }

    private unsafe delegate void AddonHoverScanner(AtkUnitBase* addon, nint titleNode, string titleText, FFXIVClientStructs.FFXIV.Common.Math.Bounds canvasBounds);

    private unsafe void CheckAddonHover(
        string addonName,
        Vector2 mousePos,
        AddonHoverScanner scanner,
        ref AtkTextNode* bestCandidate,
        ref FFXIVClientStructs.FFXIV.Common.Math.Bounds bestBounds,
        ref string bestClean,
        ref float bestArea,
        ref string bestAddon)
    {
        var addonPtr = gameGui.GetAddonByName(addonName);
        if (addonPtr.IsNull) return;

        var addon = (AtkUnitBase*)addonPtr.Address;
        if (addon == null || !addon->IsVisible || !addon->IsReady) return;

        // 檢查滑鼠是否在該 Addon 的視窗邊界內，若不在則直接略過整個視窗
        FFXIVClientStructs.FFXIV.Common.Math.Bounds winBounds = default;
        addon->GetWindowBounds(&winBounds);
        if (winBounds.Width <= 0 || winBounds.Height <= 0)
        {
            addon->GetRootBounds(&winBounds);
        }
        if (winBounds.Width > 0 && winBounds.Height > 0)
        {
            if (!IsPointInside(winBounds, mousePos)) return;
        }

        scanner(addon, 0, string.Empty, default);
    }

    private unsafe void ScanCanvasHover(
        AtkComponentJournalCanvas* canvas,
        nint titleNode,
        string titleText,
        FFXIVClientStructs.FFXIV.Common.Math.Bounds canvasBounds,
        Vector2 mousePos,
        HashSet<nint> visited,
        ref AtkTextNode* bestCandidate,
        ref FFXIVClientStructs.FFXIV.Common.Math.Bounds bestBounds,
        ref string bestClean,
        ref float bestArea,
        ref string bestAddon,
        string addonName)
    {
        if (canvas == null) return;

        ScanUldHover(&canvas->UldManager, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, addonName);
        if (canvas->AtkResNode != null)
        {
            ScanNodeHover(canvas->AtkResNode, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, addonName);
        }
        if (canvas->OwnerNode != null)
        {
            ScanNodeHover(&canvas->OwnerNode->AtkResNode, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, addonName);
        }
    }

    private unsafe void ScanUldHover(
        AtkUldManager* uldManager,
        nint titleNode,
        string titleText,
        FFXIVClientStructs.FFXIV.Common.Math.Bounds canvasBounds,
        Vector2 mousePos,
        HashSet<nint> visited,
        ref AtkTextNode* bestCandidate,
        ref FFXIVClientStructs.FFXIV.Common.Math.Bounds bestBounds,
        ref string bestClean,
        ref float bestArea,
        ref string bestAddon,
        string addonName,
        int depth = 0)
    {
        if (uldManager == null || depth > 18) return;
        if (!visited.Add((nint)uldManager)) return;

        if (uldManager->RootNode != null)
        {
            ScanNodeHover(uldManager->RootNode, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, addonName, depth + 1);
        }

        if (uldManager->NodeList != null)
        {
            for (var i = 0; i < uldManager->NodeListCount; i++)
            {
                var node = uldManager->NodeList[i];
                if (node == null) continue;
                ScanNodeHover(node, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, addonName, depth + 1);
            }
        }
    }

    private unsafe void ScanNodeHover(
        AtkResNode* node,
        nint titleNode,
        string titleText,
        FFXIVClientStructs.FFXIV.Common.Math.Bounds canvasBounds,
        Vector2 mousePos,
        HashSet<nint> visited,
        ref AtkTextNode* bestCandidate,
        ref FFXIVClientStructs.FFXIV.Common.Math.Bounds bestBounds,
        ref string bestClean,
        ref float bestArea,
        ref string bestAddon,
        string addonName,
        int depth = 0)
    {
        if (node == null || depth > 20) return;
        if (!visited.Add((nint)node)) return;

        var nodeType = node->GetNodeType();
        if (nodeType == NodeType.Text || node->Type == NodeType.Text)
        {
            var textNode = node->GetAsAtkTextNode();
            if (textNode == null) textNode = (AtkTextNode*)node;
            CheckCandidateHover(textNode, titleNode, titleText, canvasBounds, mousePos, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, addonName);
        }
        else if ((int)node->Type >= 1000 || nodeType == NodeType.Component)
        {
            var compNode = node->GetAsAtkComponentNode();
            if (compNode == null) compNode = (AtkComponentNode*)node;

            if (compNode != null && compNode->Component != null)
            {
                ScanUldHover(&compNode->Component->UldManager, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, addonName, depth + 1);
                if (compNode->Component->AtkResNode != null)
                {
                    ScanNodeHover(compNode->Component->AtkResNode, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, addonName, depth + 1);
                }

                try
                {
                    var compType = compNode->Component->GetComponentType();
                    if (compType == ComponentType.ListItemRenderer)
                    {
                        var renderer = (AtkComponentListItemRenderer*)compNode->Component;
                        if (renderer != null && renderer->ButtonTextNode != null)
                        {
                            CheckCandidateHover(renderer->ButtonTextNode, titleNode, titleText, canvasBounds, mousePos, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, addonName);
                        }
                    }
                    else if (compType == ComponentType.Button)
                    {
                        var btn = (AtkComponentButton*)compNode->Component;
                        if (btn != null && btn->ButtonTextNode != null)
                        {
                            CheckCandidateHover(btn->ButtonTextNode, titleNode, titleText, canvasBounds, mousePos, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, addonName);
                        }
                    }
                }
                catch { }
            }
        }

        // 走訪同層兄弟節點
        for (var next = node->NextSiblingNode; next != null; next = next->NextSiblingNode)
        {
            ScanNodeHover(next, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, addonName, depth);
        }
        for (var prev = node->PrevSiblingNode; prev != null; prev = prev->PrevSiblingNode)
        {
            ScanNodeHover(prev, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, addonName, depth);
        }

        // 走訪子節點鏈表
        var child = node->ChildNode;
        if (child != null)
        {
            for (var curr = child; curr != null; curr = curr->NextSiblingNode)
            {
                ScanNodeHover(curr, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, addonName, depth + 1);
            }
            for (var curr = child->PrevSiblingNode; curr != null; curr = curr->PrevSiblingNode)
            {
                ScanNodeHover(curr, titleNode, titleText, canvasBounds, mousePos, visited, ref bestCandidate, ref bestBounds, ref bestClean, ref bestArea, ref bestAddon, addonName, depth + 1);
            }
        }
    }

    private unsafe void CheckCandidateHover(
        AtkTextNode* textNode,
        nint titleNode,
        string titleText,
        FFXIVClientStructs.FFXIV.Common.Math.Bounds canvasBounds,
        Vector2 mousePos,
        ref AtkTextNode* bestCandidate,
        ref FFXIVClientStructs.FFXIV.Common.Math.Bounds bestBounds,
        ref string bestClean,
        ref float bestArea,
        ref string bestAddon,
        string addonName)
    {
        if (textNode == null) return;

        // 1. 嚴格過濾任務名稱節點 (不要翻譯任務名稱)
        if (titleNode != 0 && (nint)textNode == titleNode) return;

        // 2. 節點自身與其父階層可見性檢查
        if (!IsNodeHierarchyVisible(&textNode->AtkResNode)) return;

        // 3. 讀取節點在螢幕上的精確邊界
        FFXIVClientStructs.FFXIV.Common.Math.Bounds bounds = default;
        textNode->AtkResNode.GetBounds(&bounds);

        float minX = Math.Min(bounds.Pos1.X, bounds.Pos2.X);
        float maxX = Math.Max(bounds.Pos1.X, bounds.Pos2.X);
        float minY = Math.Min(bounds.Pos1.Y, bounds.Pos2.Y);
        float maxY = Math.Max(bounds.Pos1.Y, bounds.Pos2.Y);

        float width = maxX - minX;
        float height = maxY - minY;
        if (width < 3 || height < 3) return;

        // 4. 滑鼠命中判定 (Hit Test)
        if (mousePos.X < minX || mousePos.X > maxX || mousePos.Y < minY || mousePos.Y > maxY)
        {
            return;
        }

        // 5. 若有畫布邊界限制，且節點落在畫布外 (例如已捲動出畫布視野)，則排除
        if (canvasBounds.Width > 10 && canvasBounds.Height > 10)
        {
            if (!IsPointInside(canvasBounds, mousePos))
            {
                return;
            }
        }

        // 6. 讀取並清理文字內容
        string raw;
        try
        {
            raw = textNode->NodeText.ToString();
        }
        catch
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(raw)) return;

        var clean = ExtractCleanText(textNode, raw);
        if (string.IsNullOrWhiteSpace(clean)) return;

        // 7. 若文字內容與任務名稱相同，嚴格跳過 (不要翻譯任務名稱)
        if (!string.IsNullOrEmpty(titleText) && clean.Equals(titleText, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 8. 過濾 UI 按鈕標籤、純數字、等級等
        if (IsIgnoredOrSystemLabel(clean)) return;

        // 9. 過濾歷程回顧 (若未開啟)
        if (!config.TranslateJournalRecap && IsQuestRecapText(clean)) return;

        // 10. 確保包含可翻譯文字 (日文、漢字或英文字詞)
        if (!ContainsTranslatableCharacters(clean)) return;

        // 11. 優先挑選包圍盒面積最小者 (代表最具體精確的文字段落或行)
        float area = width * height;
        if (area < bestArea)
        {
            bestCandidate = textNode;
            bestBounds = bounds;
            bestClean = clean;
            bestArea = area;
            bestAddon = addonName;
        }
    }

    private static unsafe bool IsNodeHierarchyVisible(AtkResNode* node)
    {
        var curr = node;
        while (curr != null)
        {
            if (!curr->IsVisible()) return false;
            curr = curr->ParentNode;
        }
        return true;
    }

    private static bool IsPointInside(FFXIVClientStructs.FFXIV.Common.Math.Bounds bounds, Vector2 point)
    {
        float minX = Math.Min(bounds.Pos1.X, bounds.Pos2.X);
        float maxX = Math.Max(bounds.Pos1.X, bounds.Pos2.X);
        float minY = Math.Min(bounds.Pos1.Y, bounds.Pos2.Y);
        float maxY = Math.Max(bounds.Pos1.Y, bounds.Pos2.Y);

        return point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY;
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

                if (translationCache.ContainsKey(cleanText))
                {
                    return;
                }

                var (_, rawTranslated) = await translator.TranslateQuestTextAsync(cleanText);

                if (IsTranslationError(rawTranslated))
                {
                    failedCooldowns[cleanText] = DateTime.UtcNow.AddSeconds(5);
                    AddDiagnosticLog($"[翻譯錯誤] {cleanText.Substring(0, Math.Min(15, cleanText.Length))}: {rawTranslated}");
                    return;
                }

                string translated = CleanFallbackPrefix(rawTranslated);

                if (!string.IsNullOrWhiteSpace(translated))
                {
                    string finalResult = config.QuestEnableGlyphFolding
                        ? GlyphFolder.Fold(translated, config.CustomGlyphEntries)
                        : translated;

                    failedCooldowns.TryRemove(cleanText, out _);
                    translationCache[cleanText] = finalResult;
                    lock (translatedSet)
                    {
                        translatedSet.Add(finalResult);
                    }

                    // 如果目前懸浮中的文字剛好是本翻譯，立即同步更新
                    if (CurrentHover != null && CurrentHover.CleanText == cleanText)
                    {
                        CurrentHover.TranslatedText = finalResult;
                        CurrentHover.IsPending = false;
                    }

                    AddDiagnosticLog($"[懸停完成] {cleanText.Substring(0, Math.Min(15, cleanText.Length))} -> {finalResult.Substring(0, Math.Min(15, finalResult.Length))}");
                }
            }
            catch (Exception ex)
            {
                failedCooldowns[cleanText] = DateTime.UtcNow.AddSeconds(5);
                log.Warning(ex, $"任務懸浮翻譯失敗: {cleanText}");
                AddDiagnosticLog($"[懸停異常] {ex.Message}");
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
            catch { }
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

        if (int.TryParse(text.Replace(",", "").Replace(".", ""), out _)) return true;

        if (text.StartsWith("Lv", StringComparison.OrdinalIgnoreCase)) return true;

        if (text.Contains('/') && text.Length <= 7) return true;

        if (text.Length <= 1) return true;

        return false;
    }

    internal static bool ContainsTranslatableCharacters(string text)
    {
        foreach (var c in text)
        {
            if ((c >= 0x3040 && c <= 0x309F) ||
                (c >= 0x30A0 && c <= 0x30FF) ||
                (c >= 0x4E00 && c <= 0x9FFF))
            {
                return true;
            }
        }

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
        ClearCache();
        translationSemaphore.Dispose();
    }
}
