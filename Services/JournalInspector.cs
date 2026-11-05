using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DalamudTataru.Services;

public class JournalNodeInspectionInfo
{
    public string AddonName { get; set; } = string.Empty;
    public uint NodeId { get; set; }
    public string NodeType { get; set; } = string.Empty;
    public string ComponentType { get; set; } = string.Empty;
    public string RawText { get; set; } = string.Empty;
    public string CleanText { get; set; } = string.Empty;
    public int Length { get; set; }
    public bool HasTranslatableChars { get; set; }
    public bool IsIgnoredLabel { get; set; }
    public bool IsRecap { get; set; }
    public string CachedTranslation { get; set; } = string.Empty;
    public nint NodePointer { get; set; }
}

public class JournalInspector
{
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly Configuration config;

    private static readonly string[] TargetAddons = new[]
    {
        "JournalDetail",
        "Journal",
        "JournalAccept",
        "JournalResult",
        "QuestTodoList",
        "ScenarioTree"
    };

    public JournalInspector(IGameGui gameGui, IPluginLog log, Configuration config)
    {
        this.gameGui = gameGui;
        this.log = log;
        this.config = config;
    }

    public unsafe List<JournalNodeInspectionInfo> ScanAllJournalAddons()
    {
        var results = new List<JournalNodeInspectionInfo>();
        var visitedNodes = new HashSet<nint>();
        var visitedUlds = new HashSet<nint>();

        foreach (var addonName in TargetAddons)
        {
            var addonPtr = gameGui.GetAddonByName(addonName);
            if (addonPtr.IsNull) continue;

            var addon = (AtkUnitBase*)addonPtr.Address;
            if (addon == null || !addon->IsVisible) continue;

            // 1. 針對特定 Addon 的專屬結構欄位進行直接掃描
            if (addonName == "JournalDetail")
            {
                var detail = (AddonJournalDetail*)addon;
                ScanTextNode(detail->DutyNameTextNode, addonName, "DutyNameTextNode", results, visitedNodes);
                ScanTextNode(detail->DutyLevelTextNode, addonName, "DutyLevelTextNode", results, visitedNodes);
                ScanTextNode(detail->RewardsReceivedTextNode, addonName, "RewardsReceivedTextNode", results, visitedNodes);
                ScanTextNode(detail->AtkTextNode2A8, addonName, "AtkTextNode2A8", results, visitedNodes);
                ScanTextNode(detail->RequirementsNotMetLabelTextNode, addonName, "RequirementsNotMetLabelTextNode", results, visitedNodes);
                ScanTextNode(detail->RequirementsNotMetTextNode, addonName, "RequirementsNotMetTextNode", results, visitedNodes);
                ScanTextNode(detail->AtkTextNode2C0, addonName, "AtkTextNode2C0", results, visitedNodes);

                if (detail->JournalCanvasNode != null)
                {
                    ScanUldManager(&detail->JournalCanvasNode->UldManager, addonName, results, visitedNodes, visitedUlds);
                    if (detail->JournalCanvasNode->AtkResNode != null)
                    {
                        ScanResNode(detail->JournalCanvasNode->AtkResNode, addonName, "JournalCanvas->AtkResNode", results, visitedNodes, visitedUlds);
                    }
                    if (detail->JournalCanvasNode->OwnerNode != null)
                    {
                        ScanResNode(&detail->JournalCanvasNode->OwnerNode->AtkResNode, addonName, "JournalCanvas->OwnerNode", results, visitedNodes, visitedUlds);
                    }

                    // 依 ID 深度探測 JournalCanvas 內部的所有 TextNode 與 ItemRenderer
                    for (uint id = 1; id <= 128; id++)
                    {
                        try
                        {
                            var tNode = detail->JournalCanvasNode->GetTextNodeById(id);
                            if (tNode != null)
                            {
                                ScanTextNode(tNode, addonName, $"JournalCanvas->GetTextNodeById({id})", results, visitedNodes);
                            }
                        }
                        catch { }

                        try
                        {
                            var renderer = detail->JournalCanvasNode->GetComponentItemRendererById(id);
                            if (renderer != null)
                            {
                                if (renderer->ButtonTextNode != null)
                                {
                                    ScanTextNode(renderer->ButtonTextNode, addonName, $"JournalCanvas->ItemRenderer({id})->ButtonTextNode", results, visitedNodes);
                                }
                                ScanUldManager(&renderer->UldManager, addonName, results, visitedNodes, visitedUlds);
                                if (renderer->AtkResNode != null)
                                {
                                    ScanResNode(renderer->AtkResNode, addonName, $"JournalCanvas->ItemRenderer({id})->AtkResNode", results, visitedNodes, visitedUlds);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            else if (addonName == "JournalAccept")
            {
                var accept = (AddonJournalAccept*)addon;
                ScanTextNode(accept->QuestTitleText, addonName, "QuestTitleText", results, visitedNodes);
                ScanTextNode(accept->JournalCanvasText, addonName, "JournalCanvasText", results, visitedNodes);

                if (accept->JournalCanvas != null)
                {
                    ScanUldManager(&accept->JournalCanvas->UldManager, addonName, results, visitedNodes, visitedUlds);
                    if (accept->JournalCanvas->AtkResNode != null)
                    {
                        ScanResNode(accept->JournalCanvas->AtkResNode, addonName, "JournalAccept->CanvasResNode", results, visitedNodes, visitedUlds);
                    }
                    for (uint id = 1; id <= 64; id++)
                    {
                        try
                        {
                            var tNode = accept->JournalCanvas->GetTextNodeById(id);
                            if (tNode != null)
                            {
                                ScanTextNode(tNode, addonName, $"JournalAccept->Canvas->GetTextNodeById({id})", results, visitedNodes);
                            }
                        }
                        catch { }
                    }
                }
            }
            else if (addonName == "JournalResult")
            {
                var result = (AddonJournalResult*)addon;
                ScanTextNode(result->AtkTextNode250, addonName, "AtkTextNode250", results, visitedNodes);
                ScanTextNode(result->AtkTextNode258, addonName, "AtkTextNode258", results, visitedNodes);

                if (result->AtkComponentJournalCanvas268 != null)
                {
                    ScanUldManager(&result->AtkComponentJournalCanvas268->UldManager, addonName, results, visitedNodes, visitedUlds);
                    if (result->AtkComponentJournalCanvas268->AtkResNode != null)
                    {
                        ScanResNode(result->AtkComponentJournalCanvas268->AtkResNode, addonName, "JournalResult->CanvasResNode", results, visitedNodes, visitedUlds);
                    }
                }
            }

            // 2. 深度遍歷 addon->UldManager
            ScanUldManager(&addon->UldManager, addonName, results, visitedNodes, visitedUlds);

            // 3. 深度遍歷 addon->RootNode
            if (addon->RootNode != null)
            {
                ScanResNode(addon->RootNode, addonName, "RootNode", results, visitedNodes, visitedUlds);
            }
        }

        return results;
    }

    private unsafe void ScanTextNode(AtkTextNode* textNode, string addonName, string parentInfo, List<JournalNodeInspectionInfo> results, HashSet<nint> visitedNodes)
    {
        if (textNode == null) return;
        var ptr = (nint)textNode;
        if (!visitedNodes.Add(ptr)) return;

        string raw = string.Empty;
        try
        {
            raw = textNode->NodeText.ToString();
        }
        catch { }

        if (!string.IsNullOrEmpty(raw))
        {
            var clean = CleanSeStringText(raw);
            results.Add(new JournalNodeInspectionInfo
            {
                AddonName = addonName,
                NodeId = textNode->NodeId,
                NodeType = "TextNode",
                ComponentType = parentInfo,
                RawText = raw,
                CleanText = clean,
                Length = raw.Length,
                HasTranslatableChars = ContainsTranslatableCharacters(clean),
                IsIgnoredLabel = IsIgnoredOrSystemLabel(clean),
                IsRecap = IsQuestRecapText(clean),
                NodePointer = ptr
            });
        }
    }

    private unsafe void ScanUldManager(AtkUldManager* uldManager, string addonName, List<JournalNodeInspectionInfo> results, HashSet<nint> visitedNodes, HashSet<nint> visitedUlds, int depth = 0)
    {
        if (uldManager == null || depth > 16) return;
        if (!visitedUlds.Add((nint)uldManager)) return;

        if (uldManager->RootNode != null)
        {
            ScanResNode(uldManager->RootNode, addonName, "UldRoot", results, visitedNodes, visitedUlds, depth + 1);
        }

        if (uldManager->NodeList != null)
        {
            for (var i = 0; i < uldManager->NodeListCount; i++)
            {
                var node = uldManager->NodeList[i];
                if (node == null) continue;
                ScanResNode(node, addonName, $"NodeList[{i}]", results, visitedNodes, visitedUlds, depth + 1);
            }
        }
    }

    private unsafe void ScanResNode(AtkResNode* node, string addonName, string parentInfo, List<JournalNodeInspectionInfo> results, HashSet<nint> visitedNodes, HashSet<nint> visitedUlds, int depth = 0)
    {
        if (node == null || depth > 18) return;
        if (!visitedNodes.Add((nint)node)) return;

        if (node->Type == NodeType.Text)
        {
            ScanTextNode((AtkTextNode*)node, addonName, parentInfo, results, visitedNodes);
        }
        else if (node->Type == NodeType.Component)
        {
            var compNode = (AtkComponentNode*)node;
            if (compNode->Component != null)
            {
                var compType = compNode->Component->GetComponentType().ToString();

                ScanUldManager(&compNode->Component->UldManager, addonName, results, visitedNodes, visitedUlds, depth + 1);
                if (compNode->Component->AtkResNode != null)
                {
                    ScanResNode(compNode->Component->AtkResNode, addonName, $"{parentInfo}->CompRes({compType})", results, visitedNodes, visitedUlds, depth + 1);
                }

                // 嘗試檢查 ListItemRenderer 的 ButtonTextNode
                try
                {
                    var renderer = (AtkComponentListItemRenderer*)compNode->Component;
                    if (renderer != null && renderer->ButtonTextNode != null)
                    {
                        ScanTextNode(renderer->ButtonTextNode, addonName, $"{parentInfo}->RendererBtnText", results, visitedNodes);
                    }
                }
                catch { }
            }
        }

        // 雙向兄弟鏈表走訪
        var child = node->ChildNode;
        if (child != null)
        {
            for (var curr = child; curr != null; curr = curr->NextSiblingNode)
            {
                ScanResNode(curr, addonName, parentInfo, results, visitedNodes, visitedUlds, depth + 1);
            }
            for (var curr = child->PrevSiblingNode; curr != null; curr = curr->PrevSiblingNode)
            {
                ScanResNode(curr, addonName, parentInfo, results, visitedNodes, visitedUlds, depth + 1);
            }
        }
    }

    public static unsafe bool TestDirectSetText(nint nodePtr, string newText)
    {
        if (nodePtr == 0) return false;
        try
        {
            var textNode = (AtkTextNode*)nodePtr;
            if (textNode->Type != NodeType.Text) return false;
            textNode->SetText(newText);
            try { textNode->ResizeNodeForCurrentText(); } catch { }
            return true;
        }
        catch
        {
            return false;
        }
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
            if (c < 0x20 || c == 0x7F) continue;
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

    private static bool IsIgnoredOrSystemLabel(string text)
    {
        if (int.TryParse(text.Replace(",", "").Replace(".", ""), out _)) return true;
        if (text.StartsWith("Lv", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains('/') && text.Length <= 7) return true;
        if (text.Length <= 1) return true;
        return false;
    }

    private static bool ContainsTranslatableCharacters(string text)
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
        return false;
    }
}
