using System;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using DalamudTataru.Models;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DalamudTataru.Services;

public unsafe class DialogueWatcher : IDisposable
{
    private readonly IFramework framework;
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly Configuration config;

    public Vector2? TalkAddonPosition { get; private set; }
    public Vector2? TalkAddonSize { get; private set; }
    private bool isTalkAlphaZeroed = false;
    private bool isTalkTextNodeHidden = false;

    private string lastSpeaker = string.Empty;
    private string lastText = string.Empty;
    private DialogueType lastType = DialogueType.Talk;
    private bool wasVisible = false;
    private int absentFrames = 0;
    private const int AbsentThreshold = 3;

    public event Action<DialogueEntry>? OnDialogueChanged;
    public event Action? OnDialogueClosed;

    public DialogueWatcher(IFramework framework, IGameGui gameGui, IPluginLog log, Configuration config)
    {
        this.framework = framework;
        this.gameGui = gameGui;
        this.log = log;
        this.config = config;

        this.framework.Update += OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        bool currentVisible = false;

        // 1. Check NPC Talk dialogue
        if (config.TranslateTalk)
        {
            var talkPtr = gameGui.GetAddonByName("Talk");
            if (!talkPtr.IsNull)
            {
                var talkAddon = (AddonTalk*)talkPtr.Address;
                if (talkAddon != null)
                {
                    if (config.ReplaceGameTalkAddon)
                    {
                        if (config.ReplaceUseNativeBubble)
                        {
                            // 方案 B：保留遊戲原生泡泡框底圖，僅隱藏遊戲原生文字
                            if (isTalkAlphaZeroed)
                            {
                                talkAddon->AtkUnitBase.Alpha = 255;
                                isTalkAlphaZeroed = false;
                            }

                            if (talkAddon->AtkTextNode228 != null)
                            {
                                talkAddon->AtkTextNode228->AtkResNode.ToggleVisibility(false);
                                isTalkTextNodeHidden = true;
                            }
                        }
                        else
                        {
                            // 方案 A 或深色半透明：隱藏遊戲原生泡泡框，由 Tataru 自行繪製
                            if (talkAddon->AtkUnitBase.Alpha != 0)
                            {
                                talkAddon->AtkUnitBase.Alpha = 0;
                                isTalkAlphaZeroed = true;
                            }

                            if (isTalkTextNodeHidden && talkAddon->AtkTextNode228 != null)
                            {
                                talkAddon->AtkTextNode228->AtkResNode.ToggleVisibility(true);
                                isTalkTextNodeHidden = false;
                            }
                        }
                    }
                    else
                    {
                        if (isTalkAlphaZeroed)
                        {
                            talkAddon->AtkUnitBase.Alpha = 255;
                            isTalkAlphaZeroed = false;
                        }
                        if (isTalkTextNodeHidden && talkAddon->AtkTextNode228 != null)
                        {
                            talkAddon->AtkTextNode228->AtkResNode.ToggleVisibility(true);
                            isTalkTextNodeHidden = false;
                        }
                    }

                    if (talkAddon->AtkUnitBase.IsVisible)
                    {
                        var speaker = ReadTextNode(talkAddon->AtkTextNode220);
                        var text = ReadTextNode(talkAddon->AtkTextNode228);

                        // 必須確實有對話文字才視為對話進行中（避免對話點掉或過場過渡時 AtkUnitBase 仍殘留 IsVisible）
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            currentVisible = true;

                            if (config.ReplaceGameTalkAddon)
                            {
                                float scale = talkAddon->AtkUnitBase.Scale > 0 ? talkAddon->AtkUnitBase.Scale : 1.0f;
                                float width = 620f * scale;
                                float height = 150f * scale;

                                // 優先抓取對話文字節點 AtkTextNode228（最精確的泡泡內文邊界，避免抓到 1920 滿版容器）
                                if (talkAddon->AtkTextNode228 != null && talkAddon->AtkTextNode228->AtkResNode.Width > 100 && talkAddon->AtkTextNode228->AtkResNode.Width < 1200)
                                {
                                    width = (talkAddon->AtkTextNode228->AtkResNode.Width + 60f) * scale;
                                    height = Math.Max(140f * scale, (talkAddon->AtkTextNode228->AtkResNode.Height + 50f) * scale);
                                }
                                else if (talkAddon->AtkTextNode228 != null && talkAddon->AtkTextNode228->AtkResNode.ParentNode != null &&
                                         talkAddon->AtkTextNode228->AtkResNode.ParentNode->Width > 200 && talkAddon->AtkTextNode228->AtkResNode.ParentNode->Width < 1200)
                                {
                                    width = talkAddon->AtkTextNode228->AtkResNode.ParentNode->Width * scale;
                                    height = Math.Max(140f * scale, talkAddon->AtkTextNode228->AtkResNode.ParentNode->Height * scale);
                                }
                                else if (talkAddon->AtkUnitBase.WindowNode != null && talkAddon->AtkUnitBase.WindowNode->Width > 200 && talkAddon->AtkUnitBase.WindowNode->Width < 1200)
                                {
                                    width = talkAddon->AtkUnitBase.WindowNode->Width * scale;
                                    height = talkAddon->AtkUnitBase.WindowNode->Height * scale;
                                }
                                else if (talkAddon->AtkUnitBase.RootNode != null && talkAddon->AtkUnitBase.RootNode->Width > 200 && talkAddon->AtkUnitBase.RootNode->Width < 1200)
                                {
                                    width = talkAddon->AtkUnitBase.RootNode->Width * scale;
                                    height = talkAddon->AtkUnitBase.RootNode->Height * scale;
                                }

                                TalkAddonPosition = new Vector2(talkAddon->AtkUnitBase.X, talkAddon->AtkUnitBase.Y);
                                TalkAddonSize = new Vector2(width, height);
                            }
                            else
                            {
                                TalkAddonPosition = null;
                                TalkAddonSize = null;
                            }

                            CheckAndEmit(DialogueType.Talk, speaker, text);
                        }
                    }
                    else
                    {
                        TalkAddonPosition = null;
                        TalkAddonSize = null;
                    }
                }
            }
            else
            {
                TalkAddonPosition = null;
                TalkAddonSize = null;
            }
        }

        // 2. Check Cutscene / Movie Subtitle
        if (!currentVisible && config.TranslateSubtitle)
        {
            string[] subtitleAddons = new[] { "TalkSubtitle", "_Subtitle", "MovieSubtitle", "_MovieSubtitle" };
            foreach (var addonName in subtitleAddons)
            {
                var subPtr = gameGui.GetAddonByName(addonName);
                if (!subPtr.IsNull)
                {
                    var subAddon = (AtkUnitBase*)subPtr.Address;
                    if (subAddon != null && subAddon->IsVisible)
                    {
                        var text = ExtractFirstTextNode(subAddon);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            currentVisible = true;
                            CheckAndEmit(DialogueType.Subtitle, string.Empty, text);
                            break;
                        }
                    }
                }
            }
        }

        // 3. Check Battle Talk (Boss / combat chatter)
        if (!currentVisible && config.TranslateBattleTalk)
        {
            var battlePtr = gameGui.GetAddonByName("BattleTalk");
            if (!battlePtr.IsNull)
            {
                var battleAddon = (AtkUnitBase*)battlePtr.Address;
                if (battleAddon != null && battleAddon->IsVisible)
                {
                    var (speaker, text) = ExtractBattleTalkNodes(battleAddon);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        currentVisible = true;
                        CheckAndEmit(DialogueType.BattleTalk, speaker, text);
                    }
                }
            }
        }

        if (currentVisible)
        {
            absentFrames = 0;
            wasVisible = true;
        }
        else
        {
            TalkAddonPosition = null;
            TalkAddonSize = null;

            if (wasVisible)
            {
                absentFrames++;
                // 連續數幀皆無任何對話訊號時，判定對話已關閉／點擊結束
                if (absentFrames >= AbsentThreshold)
                {
                    wasVisible = false;
                    absentFrames = 0;
                    lastSpeaker = string.Empty;
                    lastText = string.Empty;

                    RestoreTalkAddonState();
                    OnDialogueClosed?.Invoke();
                }
            }
        }
    }

    private void RestoreTalkAddonState()
    {
        if (isTalkAlphaZeroed || isTalkTextNodeHidden)
        {
            try
            {
                var talkPtr = gameGui.GetAddonByName("Talk");
                if (!talkPtr.IsNull)
                {
                    var talkAddon = (AddonTalk*)talkPtr.Address;
                    if (talkAddon != null)
                    {
                        if (isTalkAlphaZeroed)
                        {
                            talkAddon->AtkUnitBase.Alpha = 255;
                            isTalkAlphaZeroed = false;
                        }
                        if (isTalkTextNodeHidden && talkAddon->AtkTextNode228 != null)
                        {
                            talkAddon->AtkTextNode228->AtkResNode.ToggleVisibility(true);
                            isTalkTextNodeHidden = false;
                        }
                    }
                }
            }
            catch
            {
                // Ignore addon restore error
            }
        }
    }

    private void CheckAndEmit(DialogueType type, string speaker, string text)
    {
        speaker = speaker.Trim();
        text = text.Trim();

        if (string.IsNullOrEmpty(text)) return;

        if (speaker != lastSpeaker || text != lastText || type != lastType)
        {
            lastSpeaker = speaker;
            lastText = text;
            lastType = type;

            var entry = new DialogueEntry
            {
                Type = type,
                Speaker = speaker,
                OriginalText = text,
                Timestamp = DateTime.UtcNow,
                IsTranslating = true
            };

            OnDialogueChanged?.Invoke(entry);
        }
    }

    private string ReadTextNode(AtkTextNode* textNode)
    {
        if (textNode == null) return string.Empty;
        try
        {
            var str = textNode->NodeText.ToString();
            return CleanGameText(str);
        }
        catch
        {
            return string.Empty;
        }
    }

    private string ExtractFirstTextNode(AtkUnitBase* unitBase)
    {
        if (unitBase == null) return string.Empty;
        try
        {
            return ExtractTextFromUld(&unitBase->UldManager);
        }
        catch
        {
            return string.Empty;
        }
    }

    private string ExtractTextFromUld(AtkUldManager* uldManager, int depth = 0)
    {
        if (uldManager == null || uldManager->NodeList == null || depth > 5) return string.Empty;

        try
        {
            for (var i = 0; i < uldManager->NodeListCount; i++)
            {
                var node = uldManager->NodeList[i];
                if (node == null) continue;

                if (node->Type == NodeType.Text)
                {
                    var textNode = (AtkTextNode*)node;
                    var text = textNode->NodeText.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return CleanGameText(text);
                    }
                }
                else if (node->Type == NodeType.Component)
                {
                    var compNode = (AtkComponentNode*)node;
                    if (compNode->Component != null)
                    {
                        var text = ExtractTextFromUld(&compNode->Component->UldManager, depth + 1);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
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

    private (string speaker, string text) ExtractBattleTalkNodes(AtkUnitBase* unitBase)
    {
        if (unitBase == null || unitBase->RootNode == null) return (string.Empty, string.Empty);
        string speaker = string.Empty;
        string text = string.Empty;

        try
        {
            for (var i = 0; i < unitBase->UldManager.NodeListCount; i++)
            {
                var node = unitBase->UldManager.NodeList[i];
                if (node != null && node->Type == NodeType.Text)
                {
                    var textNode = (AtkTextNode*)node;
                    var content = textNode->NodeText.ToString();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        content = CleanGameText(content);
                        if (string.IsNullOrEmpty(text))
                        {
                            text = content;
                        }
                        else if (string.IsNullOrEmpty(speaker))
                        {
                            speaker = content;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore node inspection exceptions
        }

        return (speaker, text);
    }

    private string CleanGameText(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // Remove newlines and excess whitespace
        var cleaned = input.Replace("\r\n", " ").Replace("\n", " ").Trim();
        return cleaned;
    }

    public void Dispose()
    {
        this.framework.Update -= OnFrameworkUpdate;
        RestoreTalkAddonState();
    }
}
