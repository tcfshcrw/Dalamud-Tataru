using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Dalamud.Interface.GameFonts;
using Dalamud.Plugin.Services;

namespace DalamudTataru.Services;

public static partial class GlyphFolder
{
    private const string AxisPath = "common/font/axis_12.fdt";

    private static IDataManager? dataManager;
    private static IPluginLog? pluginLog;
    private static FrozenSet<char>? supportedChars;
    private static readonly object initLock = new();

    public static void Initialize(IDataManager dm, IPluginLog log)
    {
        dataManager = dm;
        pluginLog = log;
        Warmup();
    }

    public static void Warmup()
    {
        if (supportedChars == null)
        {
            lock (initLock)
            {
                supportedChars ??= LoadAxisGlyphs();
            }
        }
    }

    private static FrozenSet<char> LoadAxisGlyphs()
    {
        try
        {
            if (dataManager == null)
            {
                return FrozenSet<char>.Empty;
            }

            var file = dataManager.GetFile(AxisPath);
            if (file == null)
            {
                pluginLog?.Warning($"[GlyphFolder] {AxisPath} not found, folding disabled.");
                return FrozenSet<char>.Empty;
            }

            var chars = new HashSet<char>();
            foreach (var glyph in new FdtReader(file.Data).Glyphs)
            {
                if (glyph.CharInt <= char.MaxValue)
                    chars.Add(glyph.Char);
            }

            pluginLog?.Information($"[GlyphFolder] Loaded {chars.Count} game AXIS glyphs.");
            return chars.ToFrozenSet();
        }
        catch (Exception ex)
        {
            pluginLog?.Warning(ex, $"[GlyphFolder] Failed to read game glyphs, folding disabled: {ex.Message}");
            return FrozenSet<char>.Empty;
        }
    }

    public static string Fold(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        Warmup();
        var glyphSet = supportedChars ?? FrozenSet<char>.Empty;

        StringBuilder? sb = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (glyphSet.Count > 0 && IsRenderable(c, glyphSet))
            {
                sb?.Append(c);
                continue;
            }

            // A lookalike that the font does carry
            if (TryLookalike(c, out var lookalike) && (glyphSet.Count == 0 || IsRenderable(lookalike, glyphSet)))
            {
                sb ??= new StringBuilder(text.Length + 8).Append(text, 0, i);
                sb.Append(lookalike);
                continue;
            }

            if (glyphSet.Count > 0)
            {
                var stripped = StripDiacritics(c, glyphSet);
                if (stripped != null)
                {
                    sb ??= new StringBuilder(text.Length + 8).Append(text, 0, i);
                    sb.Append(stripped);
                    continue;
                }
            }

            sb?.Append(c);
        }

        return sb?.ToString() ?? text;
    }

    private static bool IsRenderable(char c, FrozenSet<char> glyphSet) =>
        c < 0x80 || char.IsWhiteSpace(c) || (c >= '\uE000' && c <= '\uF8FF') || glyphSet.Contains(c);

    private static string? StripDiacritics(char c, FrozenSet<char> glyphSet)
    {
        if (CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned)
            return null;

        var sb = new StringBuilder(4);
        foreach (var d in c.ToString().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(d) != UnicodeCategory.NonSpacingMark)
                sb.Append(d);
        }

        if (sb.Length == 0 || (sb.Length == 1 && sb[0] == c))
            return null;

        var stripped = sb.ToString();
        return IsRenderable(stripped, glyphSet) ? stripped : null;
    }

    private static bool IsRenderable(string text, FrozenSet<char> glyphSet)
    {
        foreach (var c in text)
        {
            if (!IsRenderable(c, glyphSet))
                return false;
        }
        return true;
    }
}
