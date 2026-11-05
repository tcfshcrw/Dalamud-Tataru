using System;
using System.Collections.Generic;
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
    public string WatcherStatus { get; set; } = string.Empty;
    public nint NodePointer { get; set; }
}

public class JournalInspector
{
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly QuestJournalWatcher watcher;

    private static readonly string[] TargetAddons = new[]
    {
        "JournalDetail",
        "Journal",
        "JournalAccept",
        "JournalResult",
        "QuestTodoList",
        "ScenarioTree"
    };

    public JournalInspector(IGameGui gameGui, IPluginLog log, Configuration config, QuestJournalWatcher watcher)
    {
        this.gameGui = gameGui;
        this.log = log;
        this.config = config;
        this.watcher = watcher;
    }

    public unsafe List<JournalNodeInspectionInfo> ScanAllJournalAddons()
    {
        var results = new List<JournalNodeInspectionInfo>();
        var visitedResNodes = new HashSet<nint>();
        var recordedTextNodes = new HashSet<nint>();
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
                ScanTextNode(detail->DutyNameTextNode, addonName, "DutyNameTextNode", results, recordedTextNodes);
                ScanTextNode(detail->DutyLevelTextNode, addonName, "DutyLevelTextNode", results, recordedTextNodes);
                ScanTextNode(detail->RewardsReceivedTextNode, addonName, "RewardsReceivedTextNode", results, recordedTextNodes);
                ScanTextNode(detail->AtkTextNode2A8, addonName, "AtkTextNode2A8", results, recordedTextNodes);
                ScanTextNode(detail->RequirementsNotMetLabelTextNode, addonName, "RequirementsNotMetLabelTextNode", results, recordedTextNodes);
                ScanTextNode(detail->RequirementsNotMetTextNode, addonName, "RequirementsNotMetTextNode", results, recordedTextNodes);
                ScanTextNode(detail->AtkTextNode2C0, addonName, "AtkTextNode2C0", results, recordedTextNodes);

                if (detail->JournalCanvasNode != null)
                {
                    ScanUldManager(&detail->JournalCanvasNode->UldManager, addonName, "JournalCanvas->Uld", results, visitedResNodes, recordedTextNodes, visitedUlds);
                    if (detail->JournalCanvasNode->AtkResNode != null)
                    {
                        ScanResNode(detail->JournalCanvasNode->AtkResNode, addonName, "JournalCanvas->AtkResNode", results, visitedResNodes, recordedTextNodes, visitedUlds);
                    }
                    if (detail->JournalCanvasNode->OwnerNode != null)
                    {
                        ScanResNode(&detail->JournalCanvasNode->OwnerNode->AtkResNode, addonName, "JournalCanvas->OwnerNode", results, visitedResNodes, recordedTextNodes, visitedUlds);
                    }
                }
            }
            else if (addonName == "JournalAccept")
            {
                var accept = (AddonJournalAccept*)addon;
                ScanTextNode(accept->QuestTitleText, addonName, "QuestTitleText", results, recordedTextNodes);
                ScanTextNode(accept->JournalCanvasText, addonName, "JournalCanvasText", results, recordedTextNodes);

                if (accept->JournalCanvas != null)
                {
                    ScanUldManager(&accept->JournalCanvas->UldManager, addonName, "JournalAccept->CanvasUld", results, visitedResNodes, recordedTextNodes, visitedUlds);
                    if (accept->JournalCanvas->AtkResNode != null)
                    {
                        ScanResNode(accept->JournalCanvas->AtkResNode, addonName, "JournalAccept->CanvasResNode", results, visitedResNodes, recordedTextNodes, visitedUlds);
                    }
                }
            }
            else if (addonName == "JournalResult")
            {
                var result = (AddonJournalResult*)addon;
                ScanTextNode(result->AtkTextNode250, addonName, "AtkTextNode250", results, recordedTextNodes);
                ScanTextNode(result->AtkTextNode258, addonName, "AtkTextNode258", results, recordedTextNodes);

                if (result->AtkComponentJournalCanvas268 != null)
                {
                    ScanUldManager(&result->AtkComponentJournalCanvas268->UldManager, addonName, "JournalResult->CanvasUld", results, visitedResNodes, recordedTextNodes, visitedUlds);
                    if (result->AtkComponentJournalCanvas268->AtkResNode != null)
                    {
                        ScanResNode(result->AtkComponentJournalCanvas268->AtkResNode, addonName, "JournalResult->CanvasResNode", results, visitedResNodes, recordedTextNodes, visitedUlds);
                    }
                }
            }

            // 2. 深度遍歷 addon->UldManager
            ScanUldManager(&addon->UldManager, addonName, "AddonUld", results, visitedResNodes, recordedTextNodes, visitedUlds);

            // 3. 深度遍歷 addon->RootNode
            if (addon->RootNode != null)
            {
                ScanResNode(addon->RootNode, addonName, "RootNode", results, visitedResNodes, recordedTextNodes, visitedUlds);
            }
        }

        return results;
    }

    private unsafe void ScanTextNode(
        AtkTextNode* textNode,
        string addonName,
        string parentInfo,
        List<JournalNodeInspectionInfo> results,
        HashSet<nint> recordedTextNodes)
    {
        if (textNode == null) return;
        var ptr = (nint)textNode;
        if (!recordedTextNodes.Add(ptr)) return;

        string raw = string.Empty;
        try
        {
            raw = textNode->NodeText.ToString();
        }
        catch { }

        if (!string.IsNullOrEmpty(raw))
        {
            var clean = QuestJournalWatcher.ExtractCleanText(textNode, raw);
            if (string.IsNullOrEmpty(clean)) return;

            results.Add(new JournalNodeInspectionInfo
            {
                AddonName = addonName,
                NodeId = textNode->NodeId,
                NodeType = "TextNode",
                ComponentType = parentInfo,
                RawText = raw,
                CleanText = clean,
                Length = raw.Length,
                HasTranslatableChars = QuestJournalWatcher.ContainsTranslatableCharacters(clean),
                IsIgnoredLabel = QuestJournalWatcher.IsIgnoredOrSystemLabel(clean),
                IsRecap = QuestJournalWatcher.IsQuestRecapText(clean),
                WatcherStatus = watcher.GetNodeStatus(clean, raw),
                NodePointer = ptr
            });
        }
    }

    private unsafe void ScanUldManager(
        AtkUldManager* uldManager,
        string addonName,
        string uldPath,
        List<JournalNodeInspectionInfo> results,
        HashSet<nint> visitedResNodes,
        HashSet<nint> recordedTextNodes,
        HashSet<nint> visitedUlds,
        int depth = 0)
    {
        if (uldManager == null || depth > 18) return;
        if (!visitedUlds.Add((nint)uldManager)) return;

        if (uldManager->RootNode != null)
        {
            ScanResNode(uldManager->RootNode, addonName, $"{uldPath}->Root", results, visitedResNodes, recordedTextNodes, visitedUlds, depth + 1);
        }

        if (uldManager->NodeList != null)
        {
            for (var i = 0; i < uldManager->NodeListCount; i++)
            {
                var node = uldManager->NodeList[i];
                if (node == null) continue;
                ScanResNode(node, addonName, $"{uldPath}->NodeList[{i}]", results, visitedResNodes, recordedTextNodes, visitedUlds, depth + 1);
            }
        }
    }

    private unsafe void ScanResNode(
        AtkResNode* node,
        string addonName,
        string parentInfo,
        List<JournalNodeInspectionInfo> results,
        HashSet<nint> visitedResNodes,
        HashSet<nint> recordedTextNodes,
        HashSet<nint> visitedUlds,
        int depth = 0)
    {
        if (node == null || depth > 20) return;
        if (!visitedResNodes.Add((nint)node)) return;

        var nodeType = node->GetNodeType();
        if (nodeType == NodeType.Text || node->Type == NodeType.Text)
        {
            var textNode = node->GetAsAtkTextNode();
            if (textNode == null) textNode = (AtkTextNode*)node;
            ScanTextNode(textNode, addonName, parentInfo, results, recordedTextNodes);
        }
        else if ((int)node->Type >= 1000 || nodeType == NodeType.Component)
        {
            var compNode = node->GetAsAtkComponentNode();
            if (compNode == null) compNode = (AtkComponentNode*)node;

            if (compNode != null && compNode->Component != null)
            {
                var compTypeEnum = compNode->Component->GetComponentType();
                var compPath = $"{parentInfo}->Comp({compTypeEnum}:{node->NodeId})";

                ScanUldManager(&compNode->Component->UldManager, addonName, compPath, results, visitedResNodes, recordedTextNodes, visitedUlds, depth + 1);
                if (compNode->Component->AtkResNode != null)
                {
                    ScanResNode(compNode->Component->AtkResNode, addonName, $"{compPath}->Res", results, visitedResNodes, recordedTextNodes, visitedUlds, depth + 1);
                }

                try
                {
                    if (compTypeEnum == ComponentType.ListItemRenderer)
                    {
                        var renderer = (AtkComponentListItemRenderer*)compNode->Component;
                        if (renderer != null && renderer->ButtonTextNode != null)
                        {
                            ScanTextNode(renderer->ButtonTextNode, addonName, $"{compPath}->RendererBtnText", results, recordedTextNodes);
                        }
                    }
                    else if (compTypeEnum == ComponentType.Button)
                    {
                        var btn = (AtkComponentButton*)compNode->Component;
                        if (btn != null && btn->ButtonTextNode != null)
                        {
                            ScanTextNode(btn->ButtonTextNode, addonName, $"{compPath}->BtnText", results, recordedTextNodes);
                        }
                    }
                }
                catch { }
            }
        }

        // 1. 雙向走訪同層兄弟節點
        for (var next = node->NextSiblingNode; next != null; next = next->NextSiblingNode)
        {
            ScanResNode(next, addonName, parentInfo, results, visitedResNodes, recordedTextNodes, visitedUlds, depth);
        }
        for (var prev = node->PrevSiblingNode; prev != null; prev = prev->PrevSiblingNode)
        {
            ScanResNode(prev, addonName, parentInfo, results, visitedResNodes, recordedTextNodes, visitedUlds, depth);
        }

        // 2. 雙向走訪子節點鏈表
        var child = node->ChildNode;
        if (child != null)
        {
            for (var curr = child; curr != null; curr = curr->NextSiblingNode)
            {
                ScanResNode(curr, addonName, parentInfo, results, visitedResNodes, recordedTextNodes, visitedUlds, depth + 1);
            }
            for (var curr = child->PrevSiblingNode; curr != null; curr = curr->PrevSiblingNode)
            {
                ScanResNode(curr, addonName, parentInfo, results, visitedResNodes, recordedTextNodes, visitedUlds, depth + 1);
            }
        }
    }

    public static unsafe bool TestDirectSetText(nint nodePtr, string newText)
    {
        if (nodePtr == 0) return false;
        try
        {
            var textNode = (AtkTextNode*)nodePtr;
            if (textNode->GetNodeType() != NodeType.Text && textNode->Type != NodeType.Text) return false;
            textNode->SetText(newText);
            try { textNode->ResizeNodeForCurrentText(); } catch { }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
