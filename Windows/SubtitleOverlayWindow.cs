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
    private bool wasInReplaceMode = false;
    private bool needsApplyProfile = false;

    public bool IsConfigOpen { get; set; } = false;

    public void ApplyActiveProfile()
    {
        needsApplyProfile = true;
    }

    public void ResetToStandalonePosition()
    {
        needsApplyProfile = true;
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
            MinimumSize = new Vector2(200, 50),
            MaximumSize = new Vector2(2560, 1440)
        };

        Size = config.ReplaceGameTalkAddon ? config.ReplaceOverlaySize : config.CustomOverlaySize;
        SizeCondition = ImGuiCond.FirstUseEver;

        Position = config.ReplaceGameTalkAddon ? config.ReplaceOverlayPosition : config.CustomOverlayPosition;
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

            if (needsApplyProfile)
            {
                Position = config.ReplaceGameTalkAddon ? config.ReplaceOverlayPosition : config.CustomOverlayPosition;
                Size = config.ReplaceGameTalkAddon ? config.ReplaceOverlaySize : config.CustomOverlaySize;
                PositionCondition = ImGuiCond.Always;
                SizeCondition = ImGuiCond.Always;
                needsApplyProfile = false;
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

            if (config.ReplaceGameTalkAddon)
            {
                if (config.ReplaceFollowTalkAddonPosition && dialogueWatcher.TalkAddonPosition.HasValue)
                {
                    Position = dialogueWatcher.TalkAddonPosition.Value + config.ReplacePositionOffset;
                }
                else
                {
                    Position = config.ReplaceOverlayPosition;
                }

                Size = config.ReplaceOverlaySize;
                PositionCondition = ImGuiCond.Always;
                SizeCondition = ImGuiCond.Always;
                wasInReplaceMode = true;
            }
            else
            {
                if (wasInReplaceMode || needsApplyProfile)
                {
                    Position = config.CustomOverlayPosition;
                    Size = config.CustomOverlaySize;
                    PositionCondition = ImGuiCond.Always;
                    SizeCondition = ImGuiCond.Always;
                    wasInReplaceMode = false;
                    needsApplyProfile = false;
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

    private void SaveWindowBoundsIfMoved()
    {
        if (needsApplyProfile) return;

        var pos = ImGui.GetWindowPos();
        var sz = ImGui.GetWindowSize();
        if (pos.X > 0 && pos.Y > 0 && sz.X > 100 && sz.Y > 40)
        {
            if (config.ReplaceGameTalkAddon)
            {
                if (config.ReplaceOverlayPosition != pos || config.ReplaceOverlaySize != sz)
                {
                    config.ReplaceOverlayPosition = pos;
                    config.ReplaceOverlaySize = sz;
                    config.Save();
                }
            }
            else
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

    public override void Draw()
    {
        var currentFontScale = config.ReplaceGameTalkAddon ? config.ReplaceFontScale : config.FontScale;
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

            if (config.ReplaceGameTalkAddon)
            {
                ImGui.TextColored(config.TextColor, "冒險者大人，【原生對話框覆蓋模式】預覽中！\n您可以直接在此拖曳視窗移動位置、拉動邊框調整大小。\n對話結束時視窗將自動即刻消失。");
                ImGui.Spacing();
                ImGui.TextColored(config.OriginalTextColor, "冒険者さん、ネイティブ会話置換プレビュー中でっす！");
            }
            else
            {
                ImGui.TextColored(config.TextColor, "冒險者大人，【獨立字幕模式】預覽中！\n您可以拖曳移動位置或拉動邊框調整大小。\n關閉設定視窗後，視窗將自動開啟「滑鼠點擊穿透」。");
                if (config.ShowOriginalText)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(config.OriginalTextColor, "冒険者さん、設定ウィンドウが開いていまっす！");
                }
            }

            ImGui.PopTextWrapPos();
            ImGui.SetWindowFontScale(1.0f);

            // 記憶設定視窗開啟時玩家調整的位置與大小
            SaveWindowBoundsIfMoved();
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

        // 如果在設定視窗打開且有對話時玩家調整了位置/大小，也同樣記憶
        if (IsConfigOpen)
        {
            SaveWindowBoundsIfMoved();
        }
    }
}
