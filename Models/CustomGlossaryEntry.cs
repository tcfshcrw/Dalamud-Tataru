using System;

namespace DalamudTataru.Models;

[Serializable]
public class CustomGlossaryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;

    // Language Term Mappings
    public string Japanese { get; set; } = string.Empty;
    public string English { get; set; } = string.Empty;
    public string TraditionalChinese { get; set; } = string.Empty;
    public string SimplifiedChinese { get; set; } = string.Empty;

    // Scope rules
    public bool MatchSpeaker { get; set; } = true;
    public bool MatchDialogue { get; set; } = true;

    // Optional user note/tag
    public string Notes { get; set; } = string.Empty;

    public CustomGlossaryEntry Clone()
    {
        return new CustomGlossaryEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Enabled = Enabled,
            Japanese = Japanese,
            English = English,
            TraditionalChinese = TraditionalChinese,
            SimplifiedChinese = SimplifiedChinese,
            MatchSpeaker = MatchSpeaker,
            MatchDialogue = MatchDialogue,
            Notes = Notes
        };
    }
}
