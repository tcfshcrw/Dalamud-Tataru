using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using DalamudTataru.Models;

namespace DalamudTataru.Services;

public class GlossaryManager
{
    private readonly IPluginLog log;
    private readonly Dictionary<string, (string cht, string chs)> nameMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string target, string replacement)> postReplaceCht = new();
    private readonly List<(string target, string replacement)> postReplaceChs = new();

    public GlossaryManager(IPluginLog log, string pluginConfigDirectory)
    {
        this.log = log;
        LoadGlossaries(pluginConfigDirectory);
    }

    private void LoadGlossaries(string configDir)
    {
        try
        {
            // Search order: Assembly Location / Data, Config Dir / Data, App Domain Base
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var assemblyDir = Path.GetDirectoryName(typeof(GlossaryManager).Assembly.Location) ?? string.Empty;

            var possiblePaths = new[]
            {
                Path.Combine(assemblyDir, "Data"),
                Path.Combine(configDir, "Data"),
                Path.Combine(baseDir, "Data"),
                "Data"
            };

            string dataDir = string.Empty;
            foreach (var path in possiblePaths)
            {
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "jp-ch-name.json")))
                {
                    dataDir = path;
                    break;
                }
            }

            if (string.IsNullOrEmpty(dataDir))
            {
                this.log.Warning("Data directory not found for Tataru glossaries.");
                return;
            }

            this.log.Information($"Loading Tataru glossaries from: {dataDir}");

            // 1. Load Name Dictionary (jp-ch-name.json)
            var nameFile = Path.Combine(dataDir, "jp-ch-name.json");
            if (File.Exists(nameFile))
            {
                var json = File.ReadAllText(nameFile);
                var entries = JsonSerializer.Deserialize<List<List<string>>>(json);
                if (entries != null)
                {
                    foreach (var item in entries)
                    {
                        if (item.Count >= 3 && !item[0].StartsWith("//"))
                        {
                            var jp = item[0].Trim();
                            var cht = item[1].Trim();
                            var chs = item[2].Trim();
                            if (!string.IsNullOrEmpty(jp))
                            {
                                nameMap[jp] = (cht, chs);
                            }
                        }
                    }
                }
                this.log.Information($"Loaded {nameMap.Count} character/term name mappings.");
            }

            // 2. Load Post-Translation Corrections (after-translation-cht.json / chs.json)
            LoadReplacements(Path.Combine(dataDir, "after-translation-cht.json"), postReplaceCht);
            LoadReplacements(Path.Combine(dataDir, "after-translation-chs.json"), postReplaceChs);
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Failed to load Tataru glossaries.");
        }
    }

    private void LoadReplacements(string filePath, List<(string target, string replacement)> targetList)
    {
        if (!File.Exists(filePath)) return;
        try
        {
            var json = File.ReadAllText(filePath);
            var entries = JsonSerializer.Deserialize<List<List<string>>>(json);
            if (entries != null)
            {
                foreach (var item in entries)
                {
                    if (item.Count >= 2 && !item[0].StartsWith("//"))
                    {
                        var src = item[0].Trim();
                        var dst = item[1].Trim();
                        if (!string.IsNullOrEmpty(src))
                        {
                            targetList.Add((src, dst));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.log.Error(ex, $"Failed to load {filePath}");
        }
    }

    public string TranslateSpeaker(string speaker, TargetLanguage targetLang)
    {
        if (string.IsNullOrWhiteSpace(speaker)) return string.Empty;

        if (nameMap.TryGetValue(speaker, out var match))
        {
            return targetLang == TargetLanguage.TraditionalChinese ? match.cht : match.chs;
        }

        return speaker;
    }

    public string ApplyPostTranslation(string text, TargetLanguage targetLang)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var replacements = targetLang == TargetLanguage.TraditionalChinese ? postReplaceCht : postReplaceChs;
        foreach (var (target, replacement) in replacements)
        {
            if (text.Contains(target))
            {
                text = text.Replace(target, replacement);
            }
        }

        return text;
    }
}
