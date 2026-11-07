using System;
using System.Collections.Generic;
using System.Numerics;

namespace DalamudTataru.Models;

public class SelectStringOption
{
    public int Index { get; set; }
    public string OriginalText { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public bool IsTranslating { get; set; } = false;
}

public class SelectStringEntry
{
    public string AddonName { get; set; } = "SelectString";
    public string Prompt { get; set; } = string.Empty;
    public string TranslatedPrompt { get; set; } = string.Empty;
    public bool IsPromptTranslating { get; set; } = false;
    public List<SelectStringOption> Options { get; set; } = new();
    public Vector2 AddonPosition { get; set; }
    public Vector2 AddonSize { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string GetCacheKey() => $"{Prompt}::" + string.Join("|||", Options.ConvertAll(o => o.OriginalText));
}
