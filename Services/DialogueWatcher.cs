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
    public bool IsTalkBubbleDark { get; private set; } = false;
    private bool isTalkAlphaZeroed = false;
    private bool isTalkTextNodeHidden = false;
    private bool isTalkSpeakerNodeHidden = false;

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
        if (!config.Enabled)
        {
            RestoreTalkAddonState();
            TalkAddonPosition = null;
            TalkAddonSize = null;

            if (wasVisible)
            {
                wasVisible = false;
                absentFrames = 0;
                lastSpeaker = string.Empty;
                lastText = string.Empty;
                OnDialogueClosed?.Invoke();
            }
            return;
        }

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
                    if (!config.ReplaceGameTalkAddon)
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
                        if (isTalkSpeakerNodeHidden && talkAddon->AtkTextNode220 != null)
                        {
                            talkAddon->AtkTextNode220->AtkResNode.ToggleVisibility(true);
                            isTalkSpeakerNodeHidden = false;
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
                            IsTalkBubbleDark = DetectIsTalkBubbleDark(talkAddon);

                            if (config.ReplaceGameTalkAddon)
                            {
                                // 1. 先透過遊戲原生引擎 API 讀取精確螢幕邊界，再隱藏原生文字節點
                                FFXIVClientStructs.FFXIV.Common.Math.Bounds rootBounds = default;
                                FFXIVClientStructs.FFXIV.Common.Math.Bounds winBounds = default;
                                FFXIVClientStructs.FFXIV.Common.Math.Bounds textBounds = default;

                                talkAddon->AtkUnitBase.GetRootBounds(&rootBounds);
                                talkAddon->AtkUnitBase.GetWindowBounds(&winBounds);
                                if (talkAddon->AtkTextNode228 != null)
                                {
                                    talkAddon->AtkTextNode228->AtkResNode.GetBounds(&textBounds);
                                }

                                // 2. 切換原生泡泡/文字隱藏狀態
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
                                    if (config.ReplaceHideNativeSpeaker && talkAddon->AtkTextNode220 != null)
                                    {
                                        talkAddon->AtkTextNode220->AtkResNode.ToggleVisibility(false);
                                        isTalkSpeakerNodeHidden = true;
                                    }
                                    else if (!config.ReplaceHideNativeSpeaker && isTalkSpeakerNodeHidden && talkAddon->AtkTextNode220 != null)
                                    {
                                        talkAddon->AtkTextNode220->AtkResNode.ToggleVisibility(true);
                                        isTalkSpeakerNodeHidden = false;
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

                                // 3. 自動計算真實螢幕座標與寬高（支援 1080p / 2K / 4K 高解析度 UI 與任意 HUD 縮放）
                                float scale = talkAddon->AtkUnitBase.Scale > 0 ? talkAddon->AtkUnitBase.Scale : 1.0f;
                                var bBounds = rootBounds.Width > 100 && rootBounds.Height > 30 ? rootBounds : winBounds;

                                float rawRootW = talkAddon->AtkUnitBase.RootNode != null && talkAddon->AtkUnitBase.RootNode->Width > 100
                                    ? (float)talkAddon->AtkUnitBase.RootNode->Width
                                    : 688f;
                                float rawRootH = talkAddon->AtkUnitBase.RootNode != null && talkAddon->AtkUnitBase.RootNode->Height > 30
                                    ? (float)talkAddon->AtkUnitBase.RootNode->Height
                                    : 165f;

                                float bubbleW = bBounds.Width > 100 ? bBounds.Width : (rawRootW >= 1000f ? rawRootW : rawRootW * scale);
                                float bubbleH = bBounds.Height > 30 ? bBounds.Height : (rawRootH >= 240f ? rawRootH : rawRootH * scale);

                                // 在 4K 高解析度 UI 下，ULD 節點尺寸已為 2x (例如 RootNode=1376x330)，避免被重複乘上 Scale 導致變成 1927px
                                if (rawRootW >= 1000f && bubbleW > rawRootW * 1.05f)
                                {
                                    bubbleW = rawRootW;
                                    bubbleH = rawRootH;
                                }

                                float effectiveScaleX = rawRootW > 0 ? (bubbleW / rawRootW) : 1.0f;
                                float effectiveScaleY = rawRootH > 0 ? (bubbleH / rawRootH) : 1.0f;

                                Vector2 bubblePos = (bBounds.Width > 100 && (bBounds.Pos1.X != 0 || bBounds.Pos1.Y != 0))
                                    ? new Vector2(bBounds.Pos1.X, bBounds.Pos1.Y)
                                    : new Vector2(talkAddon->AtkUnitBase.X, talkAddon->AtkUnitBase.Y);

                                float textW = bubbleW * 0.82f;
                                float textH = bubbleH * 0.67f;
                                float textX = bubblePos.X + bubbleW * 0.09f;
                                float textY = bubblePos.Y + bubbleH * 0.25f;

                                if (talkAddon->AtkTextNode228 != null)
                                {
                                    float rawTextW = talkAddon->AtkTextNode228->AtkResNode.Width;
                                    float rawTextH = talkAddon->AtkTextNode228->AtkResNode.Height;

                                    if (textBounds.Width > 100 && textBounds.Height > 20)
                                    {
                                        textW = textBounds.Width;
                                        textH = textBounds.Height;
                                    }
                                    else if (rawTextW > 100 && rawTextH > 20)
                                    {
                                        textW = rawTextW * effectiveScaleX;
                                        textH = rawTextH * effectiveScaleY;
                                    }

                                    // 在 4K 高解析度 UI 下，AtkTextNode228 節點已為 2x 尺寸 (1128x220)，避免重複乘上 Scale 變成 1579px 超出泡泡框
                                    if (rawTextW >= 900f && textW > rawTextW * 1.05f)
                                    {
                                        textW = rawTextW;
                                        textH = rawTextH;
                                    }

                                    float localX = 0f;
                                    float localY = 0f;
                                    var cur = &talkAddon->AtkTextNode228->AtkResNode;
                                    int guard = 0;
                                    while (cur != null && cur != talkAddon->AtkUnitBase.RootNode && guard++ < 8)
                                    {
                                        localX += cur->X;
                                        localY += cur->Y;
                                        cur = cur->ParentNode;
                                    }

                                    if (textBounds.Width > 100 && (textBounds.Pos1.X > 0 || textBounds.Pos1.Y > 0) &&
                                        (bBounds.Width <= 100 || (textBounds.Pos1.X >= bBounds.Pos1.X - 10f && textBounds.Pos1.X < bBounds.Pos2.X)))
                                    {
                                        textX = textBounds.Pos1.X;
                                        textY = textBounds.Pos1.Y;
                                    }
                                    else
                                    {
                                        textX = bubblePos.X + localX * effectiveScaleX;
                                        textY = bubblePos.Y + localY * effectiveScaleY;
                                    }
                                }

                                if (config.ReplaceUseNativeBubble)
                                {
                                    // 方案 B：精準對齊原生泡泡內的文字節點 (AtkTextNode228)，扣除 ImGui 8px 預設內距使文字起點與換行邊界完全貼合原生對話框
                                    TalkAddonPosition = new Vector2(textX - 8f, textY - 6f);
                                    TalkAddonSize = new Vector2(textW + 16f, Math.Max(textH + 24f, 140f));
                                }
                                else
                                {
                                    // 方案 A 或深色半透明：對齊整個對話泡泡外框
                                    TalkAddonPosition = bubblePos;
                                    TalkAddonSize = new Vector2(bubbleW, Math.Max(bubbleH, 150f));
                                }
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

    public void RestoreTalkAddonState()
    {
        IsTalkBubbleDark = false;
        if (isTalkAlphaZeroed || isTalkTextNodeHidden || isTalkSpeakerNodeHidden)
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
                        if (isTalkSpeakerNodeHidden && talkAddon->AtkTextNode220 != null)
                        {
                            talkAddon->AtkTextNode220->AtkResNode.ToggleVisibility(true);
                            isTalkSpeakerNodeHidden = false;
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
                else if ((int)node->Type >= 1000 || node->GetNodeType() == NodeType.Component)
                {
                    var compNode = node->GetAsAtkComponentNode();
                    if (compNode == null) compNode = (AtkComponentNode*)node;
                    if (compNode != null && compNode->Component != null)
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

    private bool DetectIsTalkBubbleDark(AddonTalk* talkAddon)
    {
        if (talkAddon == null) return false;

        // 1. 優先檢查原版日文對話正文文字節點 (AtkTextNode228) 的文字顏色 (TextColor)
        // 淺色米白泡泡框 -> 原生文字為深灰/暗褐色 (Perceived Luminance <= 0.45)
        // 深色暗黑泡泡框 (暗黑劇情/UI深色主題) -> 原生文字為亮白色/淡黃色 (Perceived Luminance > 0.5)
        if (talkAddon->AtkTextNode228 != null)
        {
            var textColor = talkAddon->AtkTextNode228->TextColor;
            if (textColor.A > 0)
            {
                float lum = (0.299f * textColor.R + 0.587f * textColor.G + 0.114f * textColor.B) / 255.0f;
                if (lum > 0.5f) return true;
                if (lum <= 0.45f && textColor.A >= 100) return false;
            }
        }

        // 2. 輔助檢查說話者名字文字節點 (AtkTextNode220)
        if (talkAddon->AtkTextNode220 != null)
        {
            var spkColor = talkAddon->AtkTextNode220->TextColor;
            if (spkColor.A > 0)
            {
                float spkLum = (0.299f * spkColor.R + 0.587f * spkColor.G + 0.114f * spkColor.B) / 255.0f;
                if (spkLum > 0.6f) return true;
            }
        }

        // 3. 檢查 Talk Addon 的背景節點顏色與調色 (MultiplyColor)
        try
        {
            var uld = &talkAddon->AtkUnitBase.UldManager;
            if (uld != null && uld->NodeList != null)
            {
                for (var i = 0; i < uld->NodeListCount; i++)
                {
                    var node = uld->NodeList[i];
                    if (node != null && (node->Type == NodeType.Image || node->Type == NodeType.NineGrid))
                    {
                        if (node->Width >= 200 && node->Height >= 50)
                        {
                            if (node->MultiplyRed < 90 && node->MultiplyGreen < 90 && node->MultiplyBlue < 90)
                            {
                                return true;
                            }
                            if (node->Color.A > 0)
                            {
                                float bgLum = (0.299f * node->Color.R + 0.587f * node->Color.G + 0.114f * node->Color.B) / 255.0f;
                                if (bgLum < 0.35f)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // 忽略 ULD 檢查異常
        }

        return false;
    }

    public void Dispose()
    {
        this.framework.Update -= OnFrameworkUpdate;
        RestoreTalkAddonState();
    }
}
