using System;

namespace DalamudTataru.Models;

public enum DialogueType
{
    Talk,
    BattleTalk,
    Subtitle,
    Chat
}

public enum TargetLanguage
{
    TraditionalChinese,
    SimplifiedChinese
}

public enum TranslationEngine
{
    Gemini,
    LmStudio,
    Youdao,
    Google
}

public class DialogueEntry
{
    public DialogueType Type { get; set; }
    public string Speaker { get; set; } = string.Empty;
    public string OriginalText { get; set; } = string.Empty;
    public string TranslatedSpeaker { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsTranslating { get; set; } = false;

    public string GetCacheKey() => $"{Speaker}::{OriginalText}";
}
