using System;
using System.Numerics;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using DalamudTataru.Models;
using DalamudTataru.Services;
using Dalamud.Bindings.ImGui;

namespace DalamudTataru.Windows;

public class DialogueReplaceOverlayWindow : Window
{
    private readonly Configuration config;
    private readonly DialogueWatcher dialogueWatcher;
    private readonly IFontHandle? fontHandle;
    private DialogueEntry? currentEntry;
    private bool isDialogueActive = false;
    private bool needsReset = false;
    private bool needsApplyBounds = false;

    public bool IsConfigOpen { get; set; } = false;

    private static float GetGlobalScale()
    {
        float s = ImGuiHelpers.GlobalScale;
        return s > 0.1f ? s : 1.0f;
    }

    public void ResetPosition()
    {
        needsReset = true;
    }

    public void ApplyTalkAddonBounds(Vector2 pos, Vector2 size)
    {
        Position = pos;
        Size = size / GetGlobalScale();
        needsApplyBounds = true;

        config.ReplaceOverlayPosition = pos;
        config.ReplaceOverlaySize = size;
        config.ReplacePositionOffset = Vector2.Zero;
        config.ReplaceSizeOffset = Vector2.Zero;
        config.Save();
    }

    public DialogueReplaceOverlayWindow(Configuration config, DialogueWatcher dialogueWatcher, IFontHandle? fontHandle = null)
        : base("Tataru 原生對話覆蓋視窗 (開啟設定時可拖曳調整)###TataruReplaceOverlay",
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoFocusOnAppearing)
    {
        this.config = config;
        this.dialogueWatcher = dialogueWatcher;
        this.fontHandle = fontHandle;

        RespectCloseHotkey = false;
        DisableWindowSounds = true;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(200, 50),
            MaximumSize = new Vector2(3840, 2160)
        };

        Size = new Vector2(650, 180);
        SizeCondition = ImGuiCond.FirstUseEver;

        Position = new Vector2(400, 750);
        PositionCondition = ImGuiCond.FirstUseEver;

        IsOpen = false;
    }

    public void UpdateDialogue(DialogueEntry entry)
    {
        this.currentEntry = entry;
        this.isDialogueActive = true;
        this.IsOpen = true;
    }

    public void CloseDialogue()
    {
        this.isDialogueActive = false;
        this.IsOpen = false;
    }

    public override void PreDraw()
    {
        if (config.ReplaceUseNativeBubble || config.ReplaceUseParchmentTheme)
        {
            BgAlpha = 0.0f;
            ImGui.SetNextWindowBgAlpha(0.0f);
        }
        else
        {
            BgAlpha = config.WindowOpacity;
            ImGui.SetNextWindowBgAlpha(config.WindowOpacity);
        }

        float globalScale = GetGlobalScale();

        if (IsConfigOpen)
        {
            // Title bar visible for manual drag when not auto-locked to an active Talk addon
            Flags = ImGuiWindowFlags.NoScrollbar;
            if (config.ReplaceFollowTalkAddonPosition && dialogueWatcher.TalkAddonPosition.HasValue)
            {
                Flags |= ImGuiWindowFlags.NoTitleBar;
            }
            if (config.ReplaceUseNativeBubble || config.ReplaceUseParchmentTheme)
            {
                Flags |= ImGuiWindowFlags.NoBackground;
            }
        }
        else
        {
            // Borderless subtitle overlay with click-through enabled
            Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs;
            if (config.ReplaceUseNativeBubble || config.ReplaceUseParchmentTheme)
            {
                Flags |= ImGuiWindowFlags.NoBackground;
            }

            if (config.LockOverlay)
            {
                Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
            }
        }

        if (needsReset)
        {
            Position = new Vector2(400, 750);
            Size = new Vector2(650, 180);
            PositionCondition = ImGuiCond.Always;
            SizeCondition = ImGuiCond.Always;
            needsReset = false;
            return;
        }

        if (needsApplyBounds)
        {
            Position = config.ReplaceOverlayPosition;
            Size = config.ReplaceOverlaySize / globalScale;
            PositionCondition = ImGuiCond.Always;
            SizeCondition = ImGuiCond.Always;
            needsApplyBounds = false;
            return;
        }

        if (config.ReplaceFollowTalkAddonPosition && dialogueWatcher.TalkAddonPosition.HasValue)
        {
            Position = dialogueWatcher.TalkAddonPosition.Value + config.ReplacePositionOffset;
            PositionCondition = ImGuiCond.Always;
        }
        else
        {
            PositionCondition = ImGuiCond.FirstUseEver;
        }

        if (config.ReplaceAutoSyncSize && dialogueWatcher.TalkAddonSize.HasValue)
        {
            // Dalamud Window 內部會自動將 Window.Size 乘上 ImGuiHelpers.GlobalScale，因此需先除以 globalScale 才能得到精確的螢幕實體像素寬高
            Size = (dialogueWatcher.TalkAddonSize.Value + config.ReplaceSizeOffset) / globalScale;
            SizeCondition = ImGuiCond.Always;
        }
        else
        {
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        // 對話框關閉且未開啟設定視窗時，即刻關閉視窗
        if (!IsConfigOpen && !isDialogueActive)
        {
            IsOpen = false;
            return;
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
            bool useDarkText = config.ReplaceUseNativeBubble || config.ReplaceUseParchmentTheme;
            var scale = Math.Clamp(config.ReplaceFontScale, 0.5f, 3.0f);
            ImGui.SetWindowFontScale(scale);

            // 方案 B 已直接對齊遊戲原生文字框 AtkTextNode228 (其右界本身已在翻頁金箭頭左側)，僅需保留 16px 視窗內距
            float rightMargin = config.ReplaceUseNativeBubble ? 16f : 32f;
            float wrapPos = Math.Max(100f, ImGui.GetWindowWidth() - rightMargin);
            ImGui.PushTextWrapPos(wrapPos);

            // 方案 A：Tataru 仿原版羊皮紙風格繪製底圖與邊框
            if (config.ReplaceUseParchmentTheme && !config.ReplaceUseNativeBubble)
            {
                var drawList = ImGui.GetWindowDrawList();
                var min = ImGui.GetWindowPos();
                var max = min + ImGui.GetWindowSize();

                uint bgColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.92f, 0.87f, 0.96f));
                uint borderColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.35f, 0.27f, 0.20f, 0.98f));
                uint innerBorderColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.80f, 0.72f, 0.75f));

                drawList.AddRectFilled(min, max, bgColor, 16.0f);
                drawList.AddRect(min, max, borderColor, 16.0f, ImDrawFlags.None, 2.5f);
                drawList.AddRect(min + new Vector2(2, 2), max - new Vector2(2, 2), innerBorderColor, 14.0f, ImDrawFlags.None, 1.0f);
            }

            var textColor = useDarkText ? new Vector4(0.10f, 0.08f, 0.06f, 1.0f) : config.TextColor;
            var origTextColor = useDarkText ? new Vector4(0.42f, 0.38f, 0.34f, 1.0f) : config.OriginalTextColor;

            // When config is open and there is no active in-game dialogue, show an interactive preview
            if (IsConfigOpen && (!isDialogueActive || currentEntry == null))
            {
                string modeDesc = config.ReplaceUseNativeBubble 
                    ? "【原生泡泡底圖模式 (方案 B)】預覽中！\n遊戲原版米白泡泡框與金箭頭將完整保留，僅替換內文字體。"
                    : config.ReplaceUseParchmentTheme 
                        ? "【Tataru 仿原版羊皮紙風格 (方案 A)】預覽中！\n米白象牙色底框 + 復古深褐邊框，支援滑鼠任意拉伸大小。"
                        : "【現代深色半透明模式】預覽中！";

                ImGui.TextColored(textColor, $"冒險者大人，{modeDesc}\n對話結束時視窗將自動即刻消失。");

                if (config.ReplaceShowOriginalText)
                {
                    var origRatio = config.OriginalFontScaleRatio > 0.1f ? config.OriginalFontScaleRatio : 0.75f;
                    var origScale = Math.Clamp(scale * origRatio, 0.2f, 3.0f);

                    ImGui.PopTextWrapPos();
                    ImGui.SetWindowFontScale(origScale);
                    ImGui.PushTextWrapPos(wrapPos);

                    if (!config.ReplaceUseNativeBubble)
                    {
                        ImGui.Spacing();
                    }
                    ImGui.TextColored(origTextColor, "冒険者さん、ネイティブ会話置換プレビュー中でっす！");

                    ImGui.PopTextWrapPos();
                    ImGui.SetWindowFontScale(scale);
                    ImGui.PushTextWrapPos(wrapPos);
                }

                if (config.ReplaceUseParchmentTheme && !config.ReplaceUseNativeBubble)
                {
                    ImGui.Spacing();
                    ImGui.SameLine(Math.Max(0f, ImGui.GetWindowWidth() - 32));
                    ImGui.TextColored(new Vector4(0.85f, 0.65f, 0.15f, 1.0f), "◆");
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

            // 1. Translated Text
            if (currentEntry.IsTranslating)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "翻譯中...");
            }
            else
            {
                ImGui.TextColored(textColor, currentEntry.TranslatedText);
            }

            // 2. Original Japanese Text
            if (config.ReplaceShowOriginalText && !string.IsNullOrEmpty(currentEntry.OriginalText))
            {
                var origRatio = config.OriginalFontScaleRatio > 0.1f ? config.OriginalFontScaleRatio : 0.75f;
                var origScale = Math.Clamp(scale * origRatio, 0.2f, 3.0f);

                ImGui.PopTextWrapPos();
                ImGui.SetWindowFontScale(origScale);
                ImGui.PushTextWrapPos(wrapPos);

                if (!config.ReplaceUseNativeBubble)
                {
                    ImGui.Spacing();
                }
                ImGui.TextColored(origTextColor, currentEntry.OriginalText);

                ImGui.PopTextWrapPos();
                ImGui.SetWindowFontScale(scale);
                ImGui.PushTextWrapPos(wrapPos);
            }

            // 4. Golden prompt arrow cursor for Scheme A
            if (config.ReplaceUseParchmentTheme && !config.ReplaceUseNativeBubble)
            {
                ImGui.Spacing();
                ImGui.SameLine(Math.Max(0f, ImGui.GetWindowWidth() - 32));
                ImGui.TextColored(new Vector4(0.85f, 0.65f, 0.15f, 1.0f), "◆");
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
