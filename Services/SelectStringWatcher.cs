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

        // 1. Check SelectString (NPC branching dialogue choices)
        var selectStringPtr = gameGui.GetAddonByName("SelectString");
        if (!selectStringPtr.IsNull)
        {
            var addon = (AddonSelectString*)selectStringPtr.Address;
            if (addon != null && addon->AtkUnitBase.IsVisible)
            {
                var popup = (PopupMenu*)&addon->PopupMenu;
                if (ProcessAddon((AtkUnitBase*)addon, popup, "SelectString"))
                {
                    currentVisible = true;
                }
            }
        }

        // 2. Check SelectIconString (Choices with quest/action icons)
        if (!currentVisible)
        {
            var selectIconPtr = gameGui.GetAddonByName("SelectIconString");
            if (!selectIconPtr.IsNull)
            {
                var addon = (AddonSelectIconString*)selectIconPtr.Address;
                if (addon != null && addon->AtkUnitBase.IsVisible)
                {
                    var popup = (PopupMenu*)&addon->PopupMenu;
                    if (ProcessAddon((AtkUnitBase*)addon, popup, "SelectIconString"))
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

    private bool ProcessAddon(AtkUnitBase* unitBase, PopupMenu* popup, string addonName)
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
