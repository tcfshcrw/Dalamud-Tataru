using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using DalamudTataru.Models;

namespace DalamudTataru.Windows;

public class SelectStringOverlayWindow : Window
{
    private readonly Configuration config;
    private readonly IFontHandle? fontHandle;
    private SelectStringEntry? currentEntry;
    private bool isActive = false;

    public bool IsConfigOpen { get; set; } = false;
    public bool IsManualPreview { get; set; } = false;

    public SelectStringOverlayWindow(Configuration config, IFontHandle? fontHandle = null)
        : base("Tataru 對話選項翻譯視窗 (設定開啟時可自由預覽)###TataruSelectStringOverlay",
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.config = config;
        this.fontHandle = fontHandle;

        RespectCloseHotkey = false;
        DisableWindowSounds = true;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(180, 50),
            MaximumSize = new Vector2(1000, 1200)
        };

        IsOpen = false;
    }

    public void UpdateEntry(SelectStringEntry entry)
    {
        this.currentEntry = entry;
        this.isActive = true;
        this.IsOpen = true;
    }

    public void UpdatePosition(Vector2 pos, Vector2 size)
    {
        if (this.currentEntry != null)
        {
            this.currentEntry.AddonPosition = pos;
            this.currentEntry.AddonSize = size;
        }
    }

    public void Close()
    {
        this.isActive = false;
        this.IsOpen = false;
    }

    public override void PreDraw()
    {
        BgAlpha = config.WindowOpacity;
        ImGui.SetNextWindowBgAlpha(config.WindowOpacity);

        if (IsConfigOpen || IsManualPreview)
        {
            // Interactive mode in config
            Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize;
            PositionCondition = ImGuiCond.FirstUseEver;
            return;
        }

        Flags = ImGuiWindowFlags.NoTitleBar |
                ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.AlwaysAutoResize;

        if (config.ClickThrough)
        {
            Flags |= ImGuiWindowFlags.NoInputs;
        }

        if (isActive && currentEntry != null)
        {
            var viewport = ImGui.GetMainViewport();
            float screenW = viewport.Size.X;
            float scale = Math.Clamp(config.SelectStringScale, 0.5f, 2.5f);
            float estimatedW = Math.Max(260f, currentEntry.AddonSize.X) * scale;

            float targetX;
            if (config.SelectStringLockToRight)
            {
                // Default to the right side of the in-game dialogue choices box
                targetX = currentEntry.AddonPosition.X + currentEntry.AddonSize.X + 12f + config.SelectStringOffset.X;
                // If it pushes beyond the right edge of screen, flip to the left side
                if (targetX + estimatedW > screenW - 10f)
                {
                    targetX = currentEntry.AddonPosition.X - estimatedW - 12f - config.SelectStringOffset.X;
                }
            }
            else
            {
                // Left side
                targetX = currentEntry.AddonPosition.X - estimatedW - 12f - config.SelectStringOffset.X;
                if (targetX < 10f)
                {
                    targetX = currentEntry.AddonPosition.X + currentEntry.AddonSize.X + 12f + config.SelectStringOffset.X;
                }
            }

            float targetY = currentEntry.AddonPosition.Y + config.SelectStringOffset.Y;

            // Clamping within screen
            targetX = Math.Max(10f, Math.Min(targetX, screenW - 100f));
            targetY = Math.Max(10f, Math.Min(targetY, viewport.Size.Y - 100f));

            Position = new Vector2(targetX, targetY);
            PositionCondition = ImGuiCond.Always;
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
            var scale = Math.Clamp(config.SelectStringScale, 0.5f, 2.5f);
            ImGui.SetWindowFontScale(scale);

            float wrapWidth = Math.Max(220f, 300f * scale);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapWidth);

            // 1. Preview mode (in config or manual command)
            if ((IsConfigOpen || IsManualPreview) && (!isActive || currentEntry == null))
            {
                DrawPreview();
                ImGui.PopTextWrapPos();
                return;
            }

            if (currentEntry == null || currentEntry.Options.Count == 0)
            {
                ImGui.PopTextWrapPos();
                return;
            }

            // 2. Prompt Header (if any)
            if (!string.IsNullOrWhiteSpace(currentEntry.Prompt) || !string.IsNullOrWhiteSpace(currentEntry.TranslatedPrompt))
            {
                string promptDisplay;
                if (!string.IsNullOrWhiteSpace(currentEntry.TranslatedPrompt))
                {
                    promptDisplay = currentEntry.TranslatedPrompt;
                }
                else if (currentEntry.IsPromptTranslating)
                {
                    promptDisplay = $"{currentEntry.Prompt} (翻譯中...)";
                }
                else
                {
                    promptDisplay = currentEntry.Prompt;
                }

                ImGui.TextColored(config.SpeakerColor, $"💬 提示：{promptDisplay}");

                if (config.SelectStringShowOriginal && !string.IsNullOrWhiteSpace(currentEntry.Prompt) && currentEntry.Prompt != currentEntry.TranslatedPrompt)
                {
                    ImGui.TextColored(config.OriginalTextColor, $"   原：{currentEntry.Prompt}");
                }

                ImGui.Separator();
                ImGui.Spacing();
            }

            // 3. Options list
            for (var i = 0; i < currentEntry.Options.Count; i++)
            {
                var opt = currentEntry.Options[i];
                string prefix = $"{i + 1}. ";

                string optionDisplay;
                if (!string.IsNullOrWhiteSpace(opt.TranslatedText))
                {
                    optionDisplay = opt.TranslatedText;
                }
                else if (opt.IsTranslating)
                {
                    optionDisplay = $"{opt.OriginalText} (翻譯中...)";
                }
                else
                {
                    optionDisplay = opt.OriginalText;
                }

                ImGui.TextColored(config.TextColor, $"{prefix}{optionDisplay}");

                if (config.SelectStringShowOriginal && !string.IsNullOrWhiteSpace(opt.OriginalText) && opt.OriginalText != opt.TranslatedText)
                {
                    ImGui.TextColored(config.OriginalTextColor, $"   ↳ {opt.OriginalText}");
                }

                if (i < currentEntry.Options.Count - 1)
                {
                    ImGui.Spacing();
                }
            }

            ImGui.PopTextWrapPos();
        }
        finally
        {
            ImGui.SetWindowFontScale(1.0f);
            fontScope?.Dispose();
        }
    }

    private void DrawPreview()
    {
        ImGui.TextColored(config.SpeakerColor, "💬 提示：你想怎麼回答？ (選項翻譯預覽)");
        if (config.SelectStringShowOriginal)
        {
            ImGui.TextColored(config.OriginalTextColor, "   原：どう答える？");
        }
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(config.TextColor, "1. 贊成他的想法");
        if (config.SelectStringShowOriginal)
        {
            ImGui.TextColored(config.OriginalTextColor, "   ↳ その意見に賛成だ");
        }
        ImGui.Spacing();

        ImGui.TextColored(config.TextColor, "2. 委婉地表示反對");
        if (config.SelectStringShowOriginal)
        {
            ImGui.TextColored(config.OriginalTextColor, "   ↳ 少し考えさせてほしい");
        }
        ImGui.Spacing();

        ImGui.TextColored(config.TextColor, "3. 保持沉默並點頭");
        if (config.SelectStringShowOriginal)
        {
            ImGui.TextColored(config.OriginalTextColor, "   ↳ 無言で頷く");
        }
    }
}
