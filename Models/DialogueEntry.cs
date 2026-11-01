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

public enum SourceLanguage
{
    Auto,              // 自動判斷 (日/英皆可)
    Japanese,          // 日文 (日服/預設)
    English            // 英文 (美歐/國際服)
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
