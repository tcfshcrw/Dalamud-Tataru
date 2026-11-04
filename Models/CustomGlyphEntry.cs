using System;
using System.Collections.Generic;

namespace DalamudTataru.Models;

[Serializable]
public class CustomGlyphEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The missing / original character or string (e.g. "薰")
    /// </summary>
    public string SourceChar { get; set; } = string.Empty;

    /// <summary>
    /// The compatible replacement glyph / string (e.g. "薫")
    /// </summary>
    public string ReplacementChar { get; set; } = string.Empty;

    /// <summary>
    /// Optional user notes / tag
    /// </summary>
    public string Notes { get; set; } = string.Empty;

    public CustomGlyphEntry Clone()
    {
        return new CustomGlyphEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Enabled = Enabled,
            SourceChar = SourceChar,
            ReplacementChar = ReplacementChar,
            Notes = Notes
        };
    }

    /// <summary>
    /// Default presets of frequently missing Traditional Chinese characters in FFXIV AXIS font.
    /// </summary>
    public static List<CustomGlyphEntry> GetDefaultPresets()
    {
        return new List<CustomGlyphEntry>
        {
            new() { SourceChar = "薰", ReplacementChar = "薫", Notes = "薰衣草苗圃防等號" },
            new() { SourceChar = "卡", ReplacementChar = "カ", Notes = "卡牌/卡片防等號" },
            new() { SourceChar = "妳", ReplacementChar = "你", Notes = "第二人稱稱謂" },
            new() { SourceChar = "牠", ReplacementChar = "它", Notes = "動物第三人稱" },
            new() { SourceChar = "祂", ReplacementChar = "他", Notes = "神祇第三人稱" },
            new() { SourceChar = "甦", ReplacementChar = "蘇", Notes = "甦醒/復甦" },
            new() { SourceChar = "祕", ReplacementChar = "秘", Notes = "祕密/祕境" },
            new() { SourceChar = "佔", ReplacementChar = "占", Notes = "佔領/佔據" },
            new() { SourceChar = "樑", ReplacementChar = "梁", Notes = "棟樑/房樑" },
            new() { SourceChar = "舖", ReplacementChar = "舗", Notes = "店舖/店鋪" },
            new() { SourceChar = "傢", ReplacementChar = "家", Notes = "傢俱" },
            new() { SourceChar = "豔", ReplacementChar = "艶", Notes = "美豔/豔麗" },
            new() { SourceChar = "讚", ReplacementChar = "讃", Notes = "稱讚/讚賞" },
            new() { SourceChar = "迴", ReplacementChar = "回", Notes = "輪迴/迴避" },
            new() { SourceChar = "蹟", ReplacementChar = "跡", Notes = "古蹟/遺跡" },
            new() { SourceChar = "餵", ReplacementChar = "喂", Notes = "餵食" },
            new() { SourceChar = "囉", ReplacementChar = "羅", Notes = "語氣助詞" }
        };
    }
}
