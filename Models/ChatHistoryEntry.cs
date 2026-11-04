using System;
using Dalamud.Game.Text;

namespace DalamudTataru.Models;

public class ChatHistoryEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public XivChatType ChatType { get; set; }
    public string Sender { get; set; } = string.Empty;
    public string OriginalText { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
}
