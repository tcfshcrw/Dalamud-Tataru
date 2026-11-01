using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using DalamudTataru.Models;
using DalamudTataru.Services;
using Dalamud.Bindings.ImGui;

namespace DalamudTataru.Windows;

public class DialogueReplaceOverlayWindow : Window
{
    private readonly Configuration config;
    private readonly DialogueWatcher dialogueWatcher;
    private DialogueEntry? currentEntry;
    private bool isDialogueActive = false;
    private bool needsReset = false;

    public bool IsConfigOpen { get; set; } = false;

    public void ResetPosition()
    {
        needsReset = true;
    }

    public DialogueReplaceOverlayWindow(Configuration config, DialogueWatcher dialogueWatcher)
        : base("Tataru 原生對話覆蓋視窗 (開啟設定時可拖曳調整)###TataruReplaceOverlay",
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
        BgAlpha = config.WindowOpacity;
        ImGui.SetNextWindowBgAlpha(config.WindowOpacity);

        if (needsReset)
        {
            Position = new Vector2(400, 750);
            Size = new Vector2(650, 180);
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

            if (config.LockOverlay)
            {
                Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
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
            SizeCondition = ImGuiCond.FirstUseEver;

            // 對話框關閉即立刻關閉視窗
            if (!isDialogueActive)
            {
                IsOpen = false;
                return;
            }
        }
    }

    public override void Draw()
    {
        var scale = Math.Clamp(config.ReplaceFontScale, 0.5f, 3.0f);
        ImGui.SetWindowFontScale(scale);
        ImGui.PushTextWrapPos((ImGui.GetWindowWidth() - 25) / scale);

        // When config is open and there is no active in-game dialogue, show an interactive preview
        if (IsConfigOpen && (!isDialogueActive || currentEntry == null))
        {
            if (config.ShowSpeaker)
            {
                ImGui.TextColored(config.SpeakerColor, "【タタル (塔塔露)】");
            }

            ImGui.TextColored(config.TextColor, "冒險者大人，【原生對話框覆蓋模式】預覽中！\n您可以直接在此拖曳視窗移動位置、拉動邊框調整大小。\n對話結束時視窗將自動即刻消失。");
            ImGui.Spacing();
            ImGui.TextColored(config.OriginalTextColor, "冒険者さん、ネイティブ会話置換プレビュー中でっす！");

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
                if (!string.IsNullOrEmpty(currentEntry.Speaker) && currentEntry.Speaker != speaker)
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
        if (!string.IsNullOrEmpty(currentEntry.OriginalText))
        {
            ImGui.Spacing();
            ImGui.TextColored(config.OriginalTextColor, currentEntry.OriginalText);
        }

        ImGui.PopTextWrapPos();
        ImGui.SetWindowFontScale(1.0f);
    }
}
