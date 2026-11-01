using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using DalamudTataru.Models;

namespace DalamudTataru.Services;

public class GlossaryManager
{
    private readonly IPluginLog log;
    private readonly string configDir;
    private readonly Dictionary<string, (string cht, string chs)> nameMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string target, string replacement)> postReplaceCht = new();
    private readonly List<(string target, string replacement)> postReplaceChs = new();

    public int NameMapCount => nameMap.Count;
    public int PostReplaceChtCount => postReplaceCht.Count;
    public int PostReplaceChsCount => postReplaceChs.Count;

    public GlossaryManager(IPluginLog log, string pluginConfigDirectory)
    {
        this.log = log;
        this.configDir = pluginConfigDirectory;
        LoadGlossaries(pluginConfigDirectory);
    }

    private void LoadGlossaries(string configDir)
    {
        try
        {
            nameMap.Clear();
            postReplaceCht.Clear();
            postReplaceChs.Clear();

            // Search order: Config Dir / Data (user/updated), Assembly Location / Data (bundled), App Domain Base
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var assemblyDir = Path.GetDirectoryName(typeof(GlossaryManager).Assembly.Location) ?? string.Empty;

            var possiblePaths = new[]
            {
                Path.Combine(configDir, "Data"),
                Path.Combine(assemblyDir, "Data"),
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

    public async Task<(bool success, bool hasUpdated, string message)> UpdateGlossariesFromTataruAsync(bool forceReload = false)
    {
        var targetDataDir = Path.Combine(configDir, "Data");
        try
        {
            Directory.CreateDirectory(targetDataDir);
        }
        catch (Exception ex)
        {
            log.Error(ex, $"無法建立目錄：{targetDataDir}");
            return (false, false, $"無法建立目錄：{ex.Message}");
        }

        var filesToDownload = new[]
        {
            "jp-ch-name.json",
            "after-translation-cht.json",
            "after-translation-chs.json"
        };

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TataruPlugin/1.0 (Dalamud)");

        var downloadedFiles = new Dictionary<string, string>();

        foreach (var file in filesToDownload)
        {
            string[] candidateUrls = new[]
            {
                $"https://raw.githubusercontent.com/winw1010/tataru-assistant-text/main/ch/{file}",
                $"https://fastly.jsdelivr.net/gh/winw1010/tataru-assistant-text@main/ch/{file}"
            };

            string? content = null;
            Exception? lastEx = null;

            foreach (var url in candidateUrls)
            {
                try
                {
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var text = await response.Content.ReadAsStringAsync();
                        if (!string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith("["))
                        {
                            content = text;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            if (content == null)
            {
                var err = lastEx?.Message ?? "伺服器無回應或格式錯誤";
                log.Error($"下載 {file} 失敗：{err}");
                return (false, false, $"下載 {file} 失敗：{err}");
            }

            downloadedFiles[file] = content;
        }

        var assemblyDir = Path.GetDirectoryName(typeof(GlossaryManager).Assembly.Location) ?? string.Empty;
        var bundledDataDir = Path.Combine(assemblyDir, "Data");

        bool hasChanges = false;
        foreach (var kvp in downloadedFiles)
        {
            var userFilePath = Path.Combine(targetDataDir, kvp.Key);
            var bundledFilePath = Path.Combine(bundledDataDir, kvp.Key);

            string? localContent = null;
            if (File.Exists(userFilePath))
            {
                localContent = await File.ReadAllTextAsync(userFilePath);
            }
            else if (File.Exists(bundledFilePath))
            {
                localContent = await File.ReadAllTextAsync(bundledFilePath);
            }

            if (localContent == null || !string.Equals(localContent.Trim(), kvp.Value.Trim(), StringComparison.Ordinal))
            {
                hasChanges = true;
            }
        }

        if (hasChanges || forceReload)
        {
            foreach (var kvp in downloadedFiles)
            {
                var filePath = Path.Combine(targetDataDir, kvp.Key);
                await File.WriteAllTextAsync(filePath, kvp.Value);
            }

            LoadGlossaries(configDir);
        }

        if (hasChanges)
        {
            var msg = $"Tataru 文本庫已更新至最新版本！已載入 {nameMap.Count} 筆專有名詞對照、繁中校正 {postReplaceCht.Count} 筆、簡中校正 {postReplaceChs.Count} 筆。";
            log.Information(msg);
            return (true, true, msg);
        }
        else
        {
            var msg = $"Tataru 文本庫已是最新版本（目前載入 {nameMap.Count} 筆專有名詞對照），無須更新。";
            log.Information(msg);
            return (true, false, msg);
        }
    }
}
