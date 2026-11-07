using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using DalamudTataru.Models;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DalamudTataru.Services;

public unsafe class SelectStringWatcher : IDisposable
{
    private readonly IFramework framework;
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly Configuration config;

    private string lastSignature = string.Empty;
    private bool wasVisible = false;
    private int absentFrames = 0;
    private const int AbsentThreshold = 3;

    public event Action<SelectStringEntry>? OnSelectStringChanged;
    public event Action<Vector2, Vector2>? OnSelectStringPositionUpdated;
    public event Action? OnSelectStringClosed;

    public SelectStringWatcher(IFramework framework, IGameGui gameGui, IPluginLog log, Configuration config)
    {
        this.framework = framework;
        this.gameGui = gameGui;
        this.log = log;
        this.config = config;

        this.framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        this.framework.Update -= OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!config.Enabled || !config.TranslateSelectString)
        {
            if (wasVisible)
            {
                wasVisible = false;
                absentFrames = 0;
                lastSignature = string.Empty;
                OnSelectStringClosed?.Invoke();
            }
            return;
        }

        bool currentVisible = false;

        // 1. Check CutSceneSelectString (Cutscene dialogue choices, e.g. "何と言う？")
        var cutscenePtr = gameGui.GetAddonByName("CutSceneSelectString");
        if (!cutscenePtr.IsNull)
        {
            var addon = (AtkUnitBase*)cutscenePtr.Address;
            if (addon != null && addon->IsVisible)
            {
                if (ProcessCutSceneSelectString(addon))
                {
                    currentVisible = true;
                }
            }
        }

        // 2. Check SelectString (Standard NPC branching dialogue choices)
        if (!currentVisible)
        {
            var selectStringPtr = gameGui.GetAddonByName("SelectString");
            if (!selectStringPtr.IsNull)
            {
                var addon = (AddonSelectString*)selectStringPtr.Address;
                if (addon != null && addon->AtkUnitBase.IsVisible)
                {
                    var popup = (PopupMenu*)&addon->PopupMenu;
                    if (ProcessPopupMenuAddon((AtkUnitBase*)addon, popup, "SelectString"))
                    {
                        currentVisible = true;
                    }
                }
            }
        }

        // 3. Check SelectIconString (Choices with quest/action icons)
        if (!currentVisible)
        {
            var selectIconPtr = gameGui.GetAddonByName("SelectIconString");
            if (!selectIconPtr.IsNull)
            {
                var addon = (AddonSelectIconString*)selectIconPtr.Address;
                if (addon != null && addon->AtkUnitBase.IsVisible)
                {
                    var popup = (PopupMenu*)&addon->PopupMenu;
                    if (ProcessPopupMenuAddon((AtkUnitBase*)addon, popup, "SelectIconString"))
                    {
                        currentVisible = true;
                    }
                }
            }
        }

        if (currentVisible)
        {
            absentFrames = 0;
            wasVisible = true;
        }
        else if (wasVisible)
        {
            absentFrames++;
            if (absentFrames >= AbsentThreshold)
            {
                wasVisible = false;
                absentFrames = 0;
                lastSignature = string.Empty;
                OnSelectStringClosed?.Invoke();
            }
        }
    }

    private bool ProcessCutSceneSelectString(AtkUnitBase* unitBase)
    {
        if (unitBase == null) return false;

        string prompt = string.Empty;
        var options = new List<string>();
        var csAddon = (AddonCutSceneSelectString*)unitBase;

        // 1. Try reading prompt from TextLabel if available
        if (csAddon->TextLabel != null && csAddon->TextLabel->AtkTextNode != null)
        {
            var labelText = csAddon->TextLabel->AtkTextNode->NodeText.ToString();
            if (!string.IsNullOrWhiteSpace(labelText))
            {
                prompt = CleanText(QuestJournalWatcher.CleanSeStringText(labelText));
            }
        }

        // 2. Try reading options from OptionList->ItemLabels if available
        if (csAddon->OptionList != null && csAddon->OptionList->ListLength > 0 && csAddon->OptionList->ItemLabels != null)
        {
            for (var i = 0; i < csAddon->OptionList->ListLength; i++)
            {
                var text = ReadCString(csAddon->OptionList->ItemLabels[i].Value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    options.Add(text);
                }
            }
        }

        // 3. Fallback: traverse readable text nodes in ULD hierarchy
        if (options.Count == 0 || string.IsNullOrWhiteSpace(prompt))
        {
            var collectedTexts = new List<string>();
            var seen = new HashSet<nint>();
            if (unitBase->UldManager.NodeList != null)
            {
                for (var i = 0; i < unitBase->UldManager.NodeListCount; i++)
                {
                    CollectReadableTextNodes(unitBase->UldManager.NodeList[i], collectedTexts, seen);
                }
            }

            if (collectedTexts.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    prompt = collectedTexts[0];
                    if (options.Count == 0)
                    {
                        for (var i = 1; i < collectedTexts.Count; i++)
                        {
                            if (!string.IsNullOrWhiteSpace(collectedTexts[i]) && collectedTexts[i] != prompt && !options.Contains(collectedTexts[i]))
                            {
                                options.Add(collectedTexts[i]);
                            }
                        }
                    }
                }
                else if (options.Count == 0)
                {
                    foreach (var t in collectedTexts)
                    {
                        if (!string.IsNullOrWhiteSpace(t) && t != prompt && !options.Contains(t))
                        {
                            options.Add(t);
                        }
                    }
                }
            }
        }

        // If no content found, return false
        if (options.Count == 0 && string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        // Calculate position and size
        FFXIVClientStructs.FFXIV.Common.Math.Bounds rootBounds = default;
        FFXIVClientStructs.FFXIV.Common.Math.Bounds winBounds = default;
        unitBase->GetRootBounds(&rootBounds);
        unitBase->GetWindowBounds(&winBounds);

        var bBounds = rootBounds.Width > 50 && rootBounds.Height > 20 ? rootBounds : winBounds;
        float addonX = bBounds.Width > 50 ? bBounds.Pos1.X : unitBase->X;
        float addonY = bBounds.Height > 20 ? bBounds.Pos1.Y : unitBase->Y;
        float addonW = bBounds.Width > 50 ? bBounds.Width : (unitBase->RootNode != null ? unitBase->RootNode->Width * unitBase->Scale : 320f);
        float addonH = bBounds.Height > 20 ? bBounds.Height : (unitBase->RootNode != null ? unitBase->RootNode->Height * unitBase->Scale : 200f);

        var signature = $"CutSceneSelectString::{prompt}::" + string.Join("|||", options);

        if (signature != lastSignature)
        {
            lastSignature = signature;

            var entry = new SelectStringEntry
            {
                AddonName = "CutSceneSelectString",
                Prompt = prompt,
                IsPromptTranslating = !string.IsNullOrWhiteSpace(prompt),
                AddonPosition = new Vector2(addonX, addonY),
                AddonSize = new Vector2(addonW, addonH),
                Options = options.Select((opt, idx) => new SelectStringOption
                {
                    Index = idx,
                    OriginalText = opt,
                    TranslatedText = opt,
                    IsTranslating = !string.IsNullOrWhiteSpace(opt)
                }).ToList()
            };

            OnSelectStringChanged?.Invoke(entry);
        }
        else
        {
            OnSelectStringPositionUpdated?.Invoke(new Vector2(addonX, addonY), new Vector2(addonW, addonH));
        }

        return true;
    }

    private bool ProcessPopupMenuAddon(AtkUnitBase* unitBase, PopupMenu* popup, string addonName)
    {
        if (unitBase == null || popup == null) return false;
        if (popup->EntryCount <= 0 || popup->EntryNames == null) return false;

        var options = new List<string>(popup->EntryCount);
        for (var i = 0; i < popup->EntryCount; i++)
        {
            var text = ReadCString(popup->EntryNames[i].Value);
            options.Add(text);
        }

        // Do not process if all options are empty
        if (options.Count == 0 || options.All(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        var entrySet = new HashSet<string>(options.Where(s => !string.IsNullOrWhiteSpace(s)));
        var prompt = ExtractPrompt(unitBase, entrySet);

        // Calculate accurate screen bounds and position
        FFXIVClientStructs.FFXIV.Common.Math.Bounds winBounds = default;
        unitBase->GetWindowBounds(&winBounds);

        float addonX = winBounds.Width > 10 ? winBounds.Pos1.X : unitBase->X;
        float addonY = winBounds.Height > 10 ? winBounds.Pos1.Y : unitBase->Y;
        float addonW = winBounds.Width > 10 ? winBounds.Width : (unitBase->RootNode != null ? unitBase->RootNode->Width * unitBase->Scale : 250f);
        float addonH = winBounds.Height > 10 ? winBounds.Height : (unitBase->RootNode != null ? unitBase->RootNode->Height * unitBase->Scale : 200f);

        var signature = $"{addonName}::{prompt}::" + string.Join("|||", options);

        if (signature != lastSignature)
        {
            lastSignature = signature;

            var entry = new SelectStringEntry
            {
                AddonName = addonName,
                Prompt = prompt,
                IsPromptTranslating = !string.IsNullOrWhiteSpace(prompt),
                AddonPosition = new Vector2(addonX, addonY),
                AddonSize = new Vector2(addonW, addonH),
                Options = options.Select((opt, idx) => new SelectStringOption
                {
                    Index = idx,
                    OriginalText = opt,
                    TranslatedText = opt,
                    IsTranslating = !string.IsNullOrWhiteSpace(opt)
                }).ToList()
            };

            OnSelectStringChanged?.Invoke(entry);
        }
        else
        {
            OnSelectStringPositionUpdated?.Invoke(new Vector2(addonX, addonY), new Vector2(addonW, addonH));
        }

        return true;
    }

    private static void CollectReadableTextNodes(AtkResNode* node, List<string> results, HashSet<nint> seen, int depth = 0)
    {
        if (node == null || depth > 8) return;

        var addr = (nint)node;
        if (!seen.Add(addr)) return;

        if (node->Type == NodeType.Text)
        {
            var textNode = (AtkTextNode*)node;
            var raw = textNode->NodeText.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var clean = CleanText(QuestJournalWatcher.CleanSeStringText(raw));
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    results.Add(clean);
                }
            }
        }
        else if ((int)node->Type >= 1000 || node->GetNodeType() == NodeType.Component)
        {
            var compNode = node->GetAsAtkComponentNode();
            if (compNode == null) compNode = (AtkComponentNode*)node;
            if (compNode != null && compNode->Component != null)
            {
                var compUld = &compNode->Component->UldManager;
                if (compUld != null && compUld->NodeList != null)
                {
                    for (var i = 0; i < compUld->NodeListCount; i++)
                    {
                        CollectReadableTextNodes(compUld->NodeList[i], results, seen, depth + 1);
                    }
                }
            }
        }

        if (node->ChildNode != null)
        {
            for (var curr = node->ChildNode; curr != null; curr = curr->NextSiblingNode)
            {
                CollectReadableTextNodes(curr, results, seen, depth + 1);
            }
        }
    }

    private static string ReadCString(byte* ptr)
    {
        if (ptr == null || *ptr == 0) return string.Empty;
        try
        {
            var span = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(ptr);
            if (span.Length == 0) return string.Empty;
            var seString = SeString.Parse(span);
            return CleanText(QuestJournalWatcher.CleanSeStringText(seString.TextValue));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractPrompt(AtkUnitBase* unitBase, HashSet<string> entrySet)
    {
        if (unitBase == null) return string.Empty;
        try
        {
            var uld = &unitBase->UldManager;
            if (uld == null || uld->NodeList == null) return string.Empty;

            for (var i = 0; i < uld->NodeListCount; i++)
            {
                var node = uld->NodeList[i];
                if (node == null) continue;

                if (node->Type == NodeType.Text)
                {
                    var textNode = (AtkTextNode*)node;
                    var str = textNode->NodeText.ToString();
                    if (!string.IsNullOrWhiteSpace(str))
                    {
                        var clean = CleanText(QuestJournalWatcher.CleanSeStringText(str));
                        if (!string.IsNullOrWhiteSpace(clean) && !entrySet.Contains(clean))
                        {
                            return clean;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore node inspection exceptions
        }
        return string.Empty;
    }

    private static string CleanText(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input.Replace("\r\n", " ").Replace("\n", " ").Trim();
    }
}
