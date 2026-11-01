using System;
using System.Numerics;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using DalamudTataru.Models;
using Dalamud.Bindings.ImGui;

namespace DalamudTataru.Windows;

public class SubtitleOverlayWindow : Window
{
    private readonly Configuration config;
    private readonly IFontHandle? fontHandle;
    private DialogueEntry? currentEntry;
    private DateTime lastUpdateTime = DateTime.MinValue;
    private bool isDialogueActive = false;
    private bool needsReset = false;

    public bool IsConfigOpen { get; set; } = false;

    public void ResetPosition()
    {
        needsReset = true;
    }

    public void ResetToStandalonePosition()
    {
        needsReset = true;
    }

    public SubtitleOverlayWindow(Configuration config, IFontHandle? fontHandle = null) 
        : base("Tataru 獨立字幕視窗 (開啟設定時可拖曳調整)###TataruSubtitleOverlay", 
            ImGuiWindowFlags.NoScrollbar | 
            ImGuiWindowFlags.NoFocusOnAppearing)
    {
        this.config = config;
        this.fontHandle = fontHandle;

        RespectCloseHotkey = false;
        DisableWindowSounds = true;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(200, 50),
            MaximumSize = new Vector2(2560, 1440)
        };

        Size = new Vector2(600, 140);
        SizeCondition = ImGuiCond.FirstUseEver;

        Position = new Vector2(400, 750);
        PositionCondition = ImGuiCond.FirstUseEver;

        IsOpen = true;
    }

    public void UpdateDialogue(DialogueEntry entry)
    {
        this.currentEntry = entry;
        this.lastUpdateTime = DateTime.UtcNow;
        this.isDialogueActive = true;
        this.IsOpen = true;
    }

    public void CloseDialogue()
    {
        this.isDialogueActive = false;
        this.lastUpdateTime = DateTime.UtcNow;
    }

    public override void PreDraw()
    {
        BgAlpha = config.WindowOpacity;
        ImGui.SetNextWindowBgAlpha(config.WindowOpacity);

        if (needsReset)
        {
            Position = new Vector2(400, 750);
            Size = new Vector2(600, 140);
            PositionCondition = ImGuiCond.Always;
            SizeCondition = ImGuiCond.Always;
            needsReset = false;
            return;
        }

        if (IsConfigOpen)
        {
            // Title bar visible, freely movable and resizable, mouse clickable
            Flags = ImGuiWindowFlags.NoScrollbar;
            PositionCondition = ImGuiCond.FirstUseEver;
            SizeCondition = ImGuiCond.FirstUseEver;
        }
        else
        {
            // Borderless subtitle overlay with click-through enabled
            Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs;

            // Lock overlay if user configured lock
            if (config.LockOverlay)
            {
                Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
            }

            PositionCondition = ImGuiCond.FirstUseEver;
            SizeCondition = ImGuiCond.FirstUseEver;

            // Auto-hide check
            if (!isDialogueActive && currentEntry != null && config.AutoHideSeconds > 0)
            {
                var elapsed = (DateTime.UtcNow - lastUpdateTime).TotalSeconds;
                if (elapsed >= config.AutoHideSeconds)
                {
                    IsOpen = false;
                    return;
                }
            }
            else if (!isDialogueActive && currentEntry == null)
            {
                IsOpen = false;
                return;
            }
        }
    }

    public override void Draw()
    {
        IDisposable? fontScope = null;
        if (fontHandle?.Available == true)
        {
            fontScope = fontHandle.Push();
        }

        try
        {
            var scale = Math.Clamp(config.FontScale, 0.5f, 3.0f);
            ImGui.SetWindowFontScale(scale);
            float wrapPos = Math.Max(100f, ImGui.GetWindowWidth() - 35f);
            ImGui.PushTextWrapPos(wrapPos);

            // When config is open and there is no active in-game dialogue, show an interactive preview
            if (IsConfigOpen && (!isDialogueActive || currentEntry == null))
            {
                if (config.ShowSpeaker)
                {
                    ImGui.TextColored(config.SpeakerColor, "【タタル (塔塔露)】");
                }

                ImGui.TextColored(config.TextColor, "冒險者大人，【獨立字幕模式】預覽中！\n您可以拖曳移動位置或拉動邊框調整大小。\n關閉設定視窗後，視窗將自動開啟「滑鼠點擊穿透」。");
                if (config.ShowOriginalText)
                {
                    var origRatio = config.OriginalFontScaleRatio > 0.1f ? config.OriginalFontScaleRatio : 0.75f;
                    var origScale = Math.Clamp(scale * origRatio, 0.2f, 3.0f);

                    ImGui.PopTextWrapPos();
                    ImGui.SetWindowFontScale(origScale);
                    ImGui.PushTextWrapPos(wrapPos);

                    ImGui.Spacing();
                    ImGui.TextColored(config.OriginalTextColor, "冒険者さん、設定ウィンドウが開いていまっす！");

                    ImGui.PopTextWrapPos();
                    ImGui.SetWindowFontScale(scale);
                    ImGui.PushTextWrapPos(wrapPos);
                }

                ImGui.PopTextWrapPos();
                ImGui.SetWindowFontScale(1.0f);
                return;
            }

            if (currentEntry == null)
            {
                ImGui.PopTextWrapPos();
                ImGui.SetWindowFontScale(1.0f);
                return;
            }

            // 1. Speaker
            if (config.ShowSpeaker)
            {
                var speaker = !string.IsNullOrEmpty(currentEntry.TranslatedSpeaker) 
                    ? currentEntry.TranslatedSpeaker 
                    : currentEntry.Speaker;

                if (!string.IsNullOrEmpty(speaker))
                {
                    if (config.ShowOriginalText && !string.IsNullOrEmpty(currentEntry.Speaker) && currentEntry.Speaker != speaker)
                    {
                        ImGui.TextColored(config.SpeakerColor, $"【{speaker} ({currentEntry.Speaker})】");
                    }
                    else
                    {
                        ImGui.TextColored(config.SpeakerColor, $"【{speaker}】");
                    }
                }
            }

            // 2. Translated Text
            if (currentEntry.IsTranslating)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "翻譯中...");
            }
            else
            {
                ImGui.TextColored(config.TextColor, currentEntry.TranslatedText);
            }

            // 3. Original Japanese Text
            if (config.ShowOriginalText && !string.IsNullOrEmpty(currentEntry.OriginalText))
            {
                var origRatio = config.OriginalFontScaleRatio > 0.1f ? config.OriginalFontScaleRatio : 0.75f;
                var origScale = Math.Clamp(scale * origRatio, 0.2f, 3.0f);

                ImGui.PopTextWrapPos();
                ImGui.SetWindowFontScale(origScale);
                ImGui.PushTextWrapPos(wrapPos);

                ImGui.Spacing();
                ImGui.TextColored(config.OriginalTextColor, currentEntry.OriginalText);

                ImGui.PopTextWrapPos();
                ImGui.SetWindowFontScale(scale);
                ImGui.PushTextWrapPos(wrapPos);
            }

            ImGui.PopTextWrapPos();
            ImGui.SetWindowFontScale(1.0f);
        }
        finally
        {
            fontScope?.Dispose();
        }
    }
}
