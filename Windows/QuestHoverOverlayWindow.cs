using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using DalamudTataru.Models;
using DalamudTataru.Services;

namespace DalamudTataru.Windows;

public class QuestHoverOverlayWindow : Window
{
    private readonly Configuration config;
    private readonly QuestJournalWatcher questWatcher;
    private readonly GeminiTranslator translator;
    private readonly IFontHandle? fontHandle;

    public QuestHoverOverlayWindow(
        Configuration config,
        QuestJournalWatcher questWatcher,
        GeminiTranslator translator,
        IFontHandle? fontHandle = null)
        : base("Tataru 任務懸浮翻譯視窗###TataruQuestHoverOverlay",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoMove)
    {
        this.config = config;
        this.questWatcher = questWatcher;
        this.translator = translator;
        this.fontHandle = fontHandle;

        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        IsOpen = true;
    }

    public override void PreOpenCheck()
    {
        IsOpen = true;

        if (!config.Enabled || !config.QuestHoverTranslate)
        {
            return;
        }

        var mousePos = ImGui.GetIO().MousePos;
        questWatcher.UpdateHover(mousePos);
    }

    public override bool DrawConditions()
    {
        return config.Enabled && config.QuestHoverTranslate && questWatcher.CurrentHover != null;
    }

    public override void PreDraw()
    {
        var hover = questWatcher.CurrentHover;
        if (hover == null)
        {
            return;
        }

        // 在滑鼠右下方偏移顯示懸浮視窗
        var targetPos = hover.MousePos + new Vector2(18f, 18f);

        // 螢幕邊界保護，避免懸浮視窗超出視窗或螢幕下緣/右緣
        var viewport = ImGui.GetMainViewport();
        var dispPos = viewport.Pos;
        var dispSize = viewport.Size;

        float estWidth = 440f;
        float estHeight = 160f;

        if (targetPos.X + estWidth > dispPos.X + dispSize.X)
        {
            targetPos.X = Math.Max(dispPos.X + 10f, hover.MousePos.X - estWidth - 12f);
        }
        if (targetPos.Y + estHeight > dispPos.Y + dispSize.Y)
        {
            targetPos.Y = Math.Max(dispPos.Y + 10f, hover.MousePos.Y - estHeight - 12f);
        }

        Position = targetPos;
        PositionCondition = ImGuiCond.Always;

        // 視窗透明度 50%
        BgAlpha = 0.5f;
        ImGui.SetNextWindowBgAlpha(0.5f);
    }

    public override void Draw()
    {
        var hover = questWatcher.CurrentHover;
        if (hover == null) return;

        IDisposable? fontScope = null;
        if (fontHandle?.Available == true)
        {
            fontScope = fontHandle.Push();
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14f, 12f));
        // 底色為黑色，透明度 50% (Alpha = 0.5f)
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.0f, 0.0f, 0.0f, 0.5f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.25f, 0.25f, 0.3f, 0.6f));

        try
        {
            float maxWrapWidth = Math.Clamp(ImGui.GetIO().DisplaySize.X * 0.35f, 320f, 520f);
            ImGui.PushTextWrapPos(maxWrapWidth);

            // 標題欄提示
            ImGui.TextColored(new Vector4(0.4f, 0.82f, 1.0f, 1.0f), "📜 任務內容翻譯");
            ImGui.SameLine();
            ImGui.TextDisabled($"({hover.AddonName})");
            ImGui.Separator();
            ImGui.Spacing();

            // 僅呈現譯文或翻譯中狀態
            if (hover.IsPending)
            {
                ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.3f, 1.0f), "⏳ 正在翻譯任務內容中，請稍候...");
            }
            else if (!string.IsNullOrEmpty(hover.TranslatedText))
            {
                ImGui.TextColored(new Vector4(0.95f, 0.95f, 0.98f, 1.0f), hover.TranslatedText);
            }
            else
            {
                ImGui.TextColored(new Vector4(1.0f, 0.45f, 0.45f, 1.0f), "⚠️ 翻譯暫時無法取得，稍後請重試");
            }

            ImGui.PopTextWrapPos();
        }
        finally
        {
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(2);
            fontScope?.Dispose();
        }
    }
}
