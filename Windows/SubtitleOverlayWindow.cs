using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using DalamudTataru.Models;
using DalamudTataru.Services;
using Dalamud.Bindings.ImGui;

namespace DalamudTataru.Windows;

public class SubtitleOverlayWindow : Window
{
    private readonly Configuration config;
    private readonly DialogueWatcher dialogueWatcher;
    private DialogueEntry? currentEntry;
    private DateTime lastUpdateTime = DateTime.MinValue;
    private bool isDialogueActive = false;
    private bool wasReplacedLastFrame = false;
    private bool needsRestoreStandalone = false;

    public bool IsConfigOpen { get; set; } = false;

    public void ResetToStandalonePosition()
    {
        needsRestoreStandalone = true;
    }

    public SubtitleOverlayWindow(Configuration config, DialogueWatcher dialogueWatcher) 
        : base("Tataru 字幕視窗 (開啟設定時可拖曳調整)###TataruSubtitleOverlay", 
            ImGuiWindowFlags.NoScrollbar | 
            ImGuiWindowFlags.NoFocusOnAppearing)
    {
        this.config = config;
        this.dialogueWatcher = dialogueWatcher;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 60),
            MaximumSize = new Vector2(1200, 800)
        };

        Size = config.CustomOverlaySize;
        SizeCondition = ImGuiCond.FirstUseEver;

        Position = config.CustomOverlayPosition;
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
        if (config.ReplaceGameTalkAddon)
        {
            this.IsOpen = false;
        }
    }

    public override void PreDraw()
    {
        BgAlpha = config.WindowOpacity;
        ImGui.SetNextWindowBgAlpha(config.WindowOpacity);

        if (IsConfigOpen)
        {
            // Title bar visible, freely movable and resizable, mouse clickable
            Flags = ImGuiWindowFlags.NoScrollbar;

            if (wasReplacedLastFrame || needsRestoreStandalone)
            {
                Position = config.CustomOverlayPosition;
                Size = config.CustomOverlaySize;
                PositionCondition = ImGuiCond.Always;
                SizeCondition = ImGuiCond.Always;
                wasReplacedLastFrame = false;
                needsRestoreStandalone = false;
            }
            else
            {
                PositionCondition = ImGuiCond.FirstUseEver;
                SizeCondition = ImGuiCond.FirstUseEver;
            }
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

            if (config.ReplaceGameTalkAddon && isDialogueActive && currentEntry?.Type == DialogueType.Talk && dialogueWatcher.TalkAddonPosition.HasValue)
            {
                Position = dialogueWatcher.TalkAddonPosition.Value + config.ReplacePositionOffset;
                if (dialogueWatcher.TalkAddonSize.HasValue)
                {
                    var baseSize = dialogueWatcher.TalkAddonSize.Value + config.ReplaceSizeOffset;
                    Size = new Vector2(Math.Max(200f, baseSize.X), Math.Max(50f, baseSize.Y));
                }
                PositionCondition = ImGuiCond.Always;
                SizeCondition = ImGuiCond.Always;
                wasReplacedLastFrame = true;
            }
            else
            {
                if (wasReplacedLastFrame || needsRestoreStandalone)
                {
                    Position = config.CustomOverlayPosition;
                    Size = config.CustomOverlaySize;
                    PositionCondition = ImGuiCond.Always;
                    SizeCondition = ImGuiCond.Always;
                    wasReplacedLastFrame = false;
                    needsRestoreStandalone = false;
                }
                else
                {
                    PositionCondition = ImGuiCond.FirstUseEver;
                    SizeCondition = ImGuiCond.FirstUseEver;
                }
            }

            // Auto-hide check
            if (!isDialogueActive)
            {
                // 若為覆蓋模式：遊戲對話框消失即立刻關閉視窗，不等待淡出秒數
                if (config.ReplaceGameTalkAddon)
                {
                    IsOpen = false;
                    return;
                }

                // 獨立翻譯框模式：套用幾秒後自動隱藏
                if (currentEntry != null && config.AutoHideSeconds > 0)
                {
                    var elapsed = (DateTime.UtcNow - lastUpdateTime).TotalSeconds;
                    if (elapsed >= config.AutoHideSeconds)
                    {
                        IsOpen = false;
                        return;
                    }
                }
                else
                {
                    IsOpen = false;
                    return;
                }
            }
        }
    }

    public override void Draw()
    {
        bool isReplacingTalk = config.ReplaceGameTalkAddon && (isDialogueActive ? currentEntry?.Type == DialogueType.Talk : IsConfigOpen);
        var currentFontScale = isReplacingTalk ? config.ReplaceFontScale : config.FontScale;
        var scale = Math.Clamp(currentFontScale, 0.5f, 3.0f);
        ImGui.SetWindowFontScale(scale);
        ImGui.PushTextWrapPos((ImGui.GetWindowWidth() - 25) / scale);

        // When config is open and there is no active in-game dialogue, show an interactive preview
        if (IsConfigOpen && (!isDialogueActive || currentEntry == null))
        {
            if (config.ShowSpeaker)
            {
                ImGui.TextColored(config.SpeakerColor, "【タタル (塔塔露)】");
            }

            ImGui.TextColored(config.TextColor, "冒險者大人，設定視窗開啟中！\n此時字幕視窗常駐顯示，您可以拖曳移動位置或調整大小。\n關閉設定視窗後，視窗將自動開啟「滑鼠點擊穿透」。");

            if (config.ShowOriginalText || config.ReplaceGameTalkAddon)
            {
                ImGui.Spacing();
                ImGui.TextColored(config.OriginalTextColor, "冒険者さん、設定ウィンドウが開いていまっす！");
            }

            ImGui.PopTextWrapPos();
            ImGui.SetWindowFontScale(1.0f);

            // 記憶設定視窗開啟時玩家調整的獨立位置與大小
            if (!config.ReplaceGameTalkAddon && !wasReplacedLastFrame && !needsRestoreStandalone)
            {
                var pos = ImGui.GetWindowPos();
                var sz = ImGui.GetWindowSize();
                if (pos.X > 0 && pos.Y > 0 && sz.X > 100 && sz.Y > 40)
                {
                    if (config.CustomOverlayPosition != pos || config.CustomOverlaySize != sz)
                    {
                        config.CustomOverlayPosition = pos;
                        config.CustomOverlaySize = sz;
                        config.Save();
                    }
                }
            }
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
                if ((config.ShowOriginalText || config.ReplaceGameTalkAddon) && !string.IsNullOrEmpty(currentEntry.Speaker) && currentEntry.Speaker != speaker)
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
        if ((config.ShowOriginalText || config.ReplaceGameTalkAddon) && !string.IsNullOrEmpty(currentEntry.OriginalText))
        {
            ImGui.Spacing();
            ImGui.TextColored(config.OriginalTextColor, currentEntry.OriginalText);
        }

        ImGui.PopTextWrapPos();
        ImGui.SetWindowFontScale(1.0f);

        // 記憶獨立翻譯模式下的位置與大小
        if (!config.ReplaceGameTalkAddon && !wasReplacedLastFrame && !needsRestoreStandalone)
        {
            var pos = ImGui.GetWindowPos();
            var sz = ImGui.GetWindowSize();
            if (pos.X > 0 && pos.Y > 0 && sz.X > 100 && sz.Y > 40)
            {
                if (config.CustomOverlayPosition != pos || config.CustomOverlaySize != sz)
                {
                    config.CustomOverlayPosition = pos;
                    config.CustomOverlaySize = sz;
                    config.Save();
                }
            }
        }
    }
}
