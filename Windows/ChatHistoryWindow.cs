using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using DalamudTataru.Models;
using DalamudTataru.Services;

namespace DalamudTataru.Windows;

public class ChatHistoryWindow : Window
{
    private readonly Configuration config;
    private readonly ChatTranslationService chatService;
    private readonly IFontHandle? fontHandle;

    private string searchFilter = string.Empty;
    private bool autoScroll = true;

    public ChatHistoryWindow(Configuration config, ChatTranslationService chatService, IFontHandle? fontHandle = null)
        : base("Tataru 聊天與系統翻譯紀錄###TataruChatOverlayWindow", ImGuiWindowFlags.None)
    {
        this.config = config;
        this.chatService = chatService;
        this.fontHandle = fontHandle;

        Size = new Vector2(500, 320);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        IDisposable? fontScope = null;
        try
        {
            fontScope = fontHandle?.Push();
        }
        catch
        {
            // fallback
        }

        try
        {
            DrawControls();
            ImGui.Separator();
            DrawHistoryList();
        }
        finally
        {
            fontScope?.Dispose();
        }
    }

    private void DrawControls()
    {
        ImGui.SetNextItemWidth(180 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##chatFilter", "搜尋發送者或內容...", ref searchFilter, 100);

        ImGui.SameLine();
        ImGui.Checkbox("自動捲動", ref autoScroll);

        ImGui.SameLine();
        if (ImGui.Button("清空紀錄"))
        {
            chatService.ClearHistory();
        }

        ImGui.SameLine();
        var isChatEnabled = config.EnableChatTranslation;
        if (ImGui.Checkbox("啟用翻譯開關", ref isChatEnabled))
        {
            config.EnableChatTranslation = isChatEnabled;
            config.Save();
        }
    }

    private void DrawHistoryList()
    {
        var history = chatService.GetHistory();
        if (history.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "目前尚無翻譯紀錄。當遊戲對話視窗收到符合設定頻道的訊息時，將自動在此顯示。");
            return;
        }

        var filter = searchFilter.Trim();
        var items = string.IsNullOrEmpty(filter)
            ? history
            : history.Where(e =>
                e.Sender.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                e.OriginalText.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                e.TranslatedText.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        if (ImGui.BeginChild("##chatHistoryList", new Vector2(-1, -1), false, ImGuiWindowFlags.HorizontalScrollbar))
        {
            foreach (var item in items)
            {
                var timeStr = item.Timestamp.ToString("HH:mm:ss");
                var badgeName = ChatChannelHelper.GetDisplayName(item.ChatType);
                var badgeColor = ChatChannelHelper.GetChannelColor(item.ChatType);

                // Time
                ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 1.0f), $"[{timeStr}]");
                ImGui.SameLine();

                // Channel badge
                ImGui.TextColored(badgeColor, $"[{badgeName}]");
                ImGui.SameLine();

                // Sender
                if (!string.IsNullOrEmpty(item.Sender))
                {
                    ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.4f, 1.0f), $"{item.Sender}:");
                    ImGui.SameLine();
                }

                // Translated Text
                ImGui.TextUnformatted(item.TranslatedText);

                // Sub-line showing original if different
                if (!config.ChatHideOriginal && !string.IsNullOrEmpty(item.OriginalText) &&
                    !item.OriginalText.Equals(item.TranslatedText, StringComparison.OrdinalIgnoreCase))
                {
                    ImGui.Indent(20f);
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 0.85f), $"↳ 原文: {item.OriginalText}");
                    ImGui.Unindent(20f);
                }

                ImGui.Spacing();
            }

            if (autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
            {
                ImGui.SetScrollHereY(1.0f);
            }
        }
        ImGui.EndChild();
    }
}
