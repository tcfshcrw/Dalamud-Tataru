using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using DalamudTataru.Models;

namespace DalamudTataru.Services;

public class GlossaryManager
{
    private readonly Configuration? config;
    private readonly IPluginLog? log;
    private readonly string configDir;
    private readonly Dictionary<string, (string cht, string chs)> nameMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string target, string replacement)> postReplaceCht = new();
    private readonly List<(string target, string replacement)> postReplaceChs = new();

    private int enNameCount = 0;

    public int NameMapCount => nameMap.Count;
    public int EnNameMapCount => enNameCount;
    public int PostReplaceChtCount => postReplaceCht.Count;
    public int PostReplaceChsCount => postReplaceChs.Count;

    public string CustomGlossaryFilePath => Path.Combine(configDir, "CustomGlossary.json");
    public string ConfigDirectory => configDir;

    public GlossaryManager(Configuration? config, IPluginLog? log, string pluginConfigDirectory)
    {
        this.config = config;
        this.log = log;
        this.configDir = pluginConfigDirectory;
        LoadGlossaries(pluginConfigDirectory);
        LoadCustomGlossaryOnStartup();
    }

    public GlossaryManager(IPluginLog? log, string pluginConfigDirectory) : this(null, log, pluginConfigDirectory)
    {
    }

    private void LoadGlossaries(string configDir)
    {
        try
        {
            nameMap.Clear();
            enNameCount = 0;
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
                this.log?.Warning("Data directory not found for Tataru glossaries.");
                return;
            }

            this.log?.Information($"Loading Tataru glossaries from: {dataDir}");

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
                this.log?.Information($"Loaded {nameMap.Count} Japanese character/term name mappings.");
            }

            // 2. Load English Name Dictionary (en-ch-name.json)
            var enNameFile = Path.Combine(dataDir, "en-ch-name.json");
            if (!File.Exists(enNameFile) && !string.IsNullOrEmpty(assemblyDir))
            {
                var bundledEn = Path.Combine(assemblyDir, "Data", "en-ch-name.json");
                if (File.Exists(bundledEn)) enNameFile = bundledEn;
            }
            if (!File.Exists(enNameFile) && Directory.Exists("Data") && File.Exists(Path.Combine("Data", "en-ch-name.json")))
            {
                enNameFile = Path.Combine("Data", "en-ch-name.json");
            }

            if (File.Exists(enNameFile))
            {
                var json = File.ReadAllText(enNameFile);
                var entries = JsonSerializer.Deserialize<List<List<string>>>(json);
                if (entries != null)
                {
                    foreach (var item in entries)
                    {
                        if (item.Count >= 3 && !item[0].StartsWith("//"))
                        {
                            var en = item[0].Trim();
                            var cht = item[1].Trim();
                            var chs = item[2].Trim();
                            if (!string.IsNullOrEmpty(en))
                            {
                                nameMap[en] = (cht, chs);
                                enNameCount++;
                            }
                        }
                    }
                    this.log?.Information($"Loaded {enNameCount} English character/term name mappings.");
                }
            }

            // 3. Load Post-Translation Corrections (after-translation-cht.json / chs.json)
            LoadReplacements(Path.Combine(dataDir, "after-translation-cht.json"), postReplaceCht);
            LoadReplacements(Path.Combine(dataDir, "after-translation-chs.json"), postReplaceChs);
        }
        catch (Exception ex)
        {
            this.log?.Error(ex, "Failed to load Tataru glossaries.");
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
            this.log?.Error(ex, $"Failed to load {filePath}");
        }
    }

    public string TranslateSpeaker(string speaker, TargetLanguage targetLang)
    {
        if (string.IsNullOrWhiteSpace(speaker)) return string.Empty;

        // 1. Check user custom glossary first (highest priority)
        if (config?.EnableCustomGlossary == true && config.CustomGlossaryEntries != null)
        {
            var trimmed = speaker.Trim();
            foreach (var entry in config!.CustomGlossaryEntries)
            {
                if (!entry.Enabled || !entry.MatchSpeaker) continue;

                bool isMatch = false;
                if (!string.IsNullOrWhiteSpace(entry.Japanese) && string.Equals(trimmed, entry.Japanese.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    isMatch = true;
                }
                else if (!string.IsNullOrWhiteSpace(entry.English) && string.Equals(trimmed, entry.English.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    isMatch = true;
                }
                else if (!string.IsNullOrWhiteSpace(entry.TraditionalChinese) && string.Equals(trimmed, entry.TraditionalChinese.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    isMatch = true;
                }
                else if (!string.IsNullOrWhiteSpace(entry.SimplifiedChinese) && string.Equals(trimmed, entry.SimplifiedChinese.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    isMatch = true;
                }

                if (isMatch)
                {
                    var customResult = targetLang == TargetLanguage.TraditionalChinese ? entry.TraditionalChinese : entry.SimplifiedChinese;
                    if (!string.IsNullOrWhiteSpace(customResult))
                    {
                        return customResult.Trim();
                    }
                }
            }
        }

        // 2. Tataru official downloaded name dictionary
        if (nameMap.TryGetValue(speaker, out var match))
        {
            return targetLang == TargetLanguage.TraditionalChinese ? match.cht : match.chs;
        }

        return speaker;
    }

    public string ApplyPostTranslation(string text, TargetLanguage targetLang)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // Middle dot punctuation normalization (U+30FB / U+2022 / U+2027 -> Latin-1 U+00B7)
        text = text.Replace('・', '·').Replace('•', '·').Replace('‧', '·');

        // 1. Tataru built-in / downloaded post replacements
        var replacements = targetLang == TargetLanguage.TraditionalChinese ? postReplaceCht : postReplaceChs;
        foreach (var (target, replacement) in replacements)
        {
            if (text.Contains(target))
            {
                text = text.Replace(target, replacement);
            }
        }

        // 2. Custom Glossary Post Corrections (MatchDialogue)
        if (config?.EnableCustomGlossary == true && config.CustomGlossaryEntries != null)
        {
            foreach (var entry in config!.CustomGlossaryEntries)
            {
                if (!entry.Enabled || !entry.MatchDialogue) continue;

                var targetReplacement = targetLang == TargetLanguage.TraditionalChinese ? entry.TraditionalChinese : entry.SimplifiedChinese;
                if (string.IsNullOrWhiteSpace(targetReplacement)) continue;
                targetReplacement = targetReplacement.Trim();

                // If Japanese term appears in translated text, replace it
                if (!string.IsNullOrWhiteSpace(entry.Japanese) && text.Contains(entry.Japanese.Trim()))
                {
                    text = text.Replace(entry.Japanese.Trim(), targetReplacement);
                }

                // If English term appears in translated text, replace it
                if (!string.IsNullOrWhiteSpace(entry.English) && text.Contains(entry.English.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    text = Regex.Replace(text, Regex.Escape(entry.English.Trim()), targetReplacement, RegexOptions.IgnoreCase);
                }

                // If target is Traditional Chinese but output had Simplified Chinese
                if (targetLang == TargetLanguage.TraditionalChinese && !string.IsNullOrWhiteSpace(entry.SimplifiedChinese) && text.Contains(entry.SimplifiedChinese.Trim()))
                {
                    text = text.Replace(entry.SimplifiedChinese.Trim(), targetReplacement);
                }
                // If target is Simplified Chinese but output had Traditional Chinese
                else if (targetLang == TargetLanguage.SimplifiedChinese && !string.IsNullOrWhiteSpace(entry.TraditionalChinese) && text.Contains(entry.TraditionalChinese.Trim()))
                {
                    text = text.Replace(entry.TraditionalChinese.Trim(), targetReplacement);
                }
            }
        }

        return text;
    }

    /// <summary>
    /// Generates prompt injection rules for relevant custom terms to feed into Gemini & LM Studio.
    /// </summary>
    public string GetCustomGlossaryPrompt(string? speaker, string text, TargetLanguage targetLang)
    {
        if (config?.EnableCustomGlossary != true || config.CustomGlossaryEntries == null || config.CustomGlossaryEntries.Count == 0)
        {
            return string.Empty;
        }

        var relevantRules = new List<string>();
        if (config?.ProtectPlayerName == true)
        {
            relevantRules.Add($"- {PlayerNameHelper.PlayerPlaceholder} => {PlayerNameHelper.PlayerPlaceholder} (玩家角色名稱標記，請在輸出中原樣保留，嚴禁音譯為漢字)");
        }
        foreach (var entry in config!.CustomGlossaryEntries)
        {
            if (!entry.Enabled) continue;

            var targetTranslation = targetLang == TargetLanguage.TraditionalChinese ? entry.TraditionalChinese : entry.SimplifiedChinese;
            if (string.IsNullOrWhiteSpace(targetTranslation)) continue;

            bool isRelevant = false;
            if (!string.IsNullOrEmpty(speaker))
            {
                if (!string.IsNullOrEmpty(entry.Japanese) && speaker.Contains(entry.Japanese.Trim(), StringComparison.OrdinalIgnoreCase)) isRelevant = true;
                if (!string.IsNullOrEmpty(entry.English) && speaker.Contains(entry.English.Trim(), StringComparison.OrdinalIgnoreCase)) isRelevant = true;
            }

            if (!isRelevant && !string.IsNullOrEmpty(text))
            {
                if (!string.IsNullOrEmpty(entry.Japanese) && text.Contains(entry.Japanese.Trim())) isRelevant = true;
                if (!string.IsNullOrEmpty(entry.English) && text.Contains(entry.English.Trim(), StringComparison.OrdinalIgnoreCase)) isRelevant = true;
            }

            if (isRelevant)
            {
                var src = !string.IsNullOrEmpty(entry.Japanese) ? entry.Japanese.Trim() : entry.English.Trim();
                if (!string.IsNullOrEmpty(entry.Japanese) && !string.IsNullOrEmpty(entry.English) && !entry.Japanese.Equals(entry.English, StringComparison.OrdinalIgnoreCase))
                {
                    src = $"{entry.Japanese.Trim()} / {entry.English.Trim()}";
                }
                relevantRules.Add($"- {src} => {targetTranslation.Trim()}");
            }
        }

        if (relevantRules.Count == 0) return string.Empty;

        return "\n【專有名詞與人物名稱強制對照表（請嚴格遵守）】：\n" + string.Join("\n", relevantRules) + "\n";
    }

    /// <summary>
    /// Default preset dictionary of popular FFXIV characters and lore terms.
    /// </summary>
    public static List<CustomGlossaryEntry> GetDefaultPresets()
    {
        return new List<CustomGlossaryEntry>
        {
            new() { Japanese = "タタル", English = "Tataru", TraditionalChinese = "塔塔露", SimplifiedChinese = "塔塔露", MatchSpeaker = true, MatchDialogue = true, Notes = "拂曉幹部" },
            new() { Japanese = "アルフィノ", English = "Alphinaud", TraditionalChinese = "阿爾菲諾", SimplifiedChinese = "阿尔菲诺", MatchSpeaker = true, MatchDialogue = true, Notes = "拂曉幹部" },
            new() { Japanese = "アリゼー", English = "Alisaie", TraditionalChinese = "阿莉塞", SimplifiedChinese = "阿莉塞", MatchSpeaker = true, MatchDialogue = true, Notes = "拂曉幹部" },
            new() { Japanese = "ヤ・シュトラ", English = "Y'shtola", TraditionalChinese = "雅·修特拉", SimplifiedChinese = "雅·修特拉", MatchSpeaker = true, MatchDialogue = true, Notes = "拂曉幹部" },
            new() { Japanese = "サンクレッド", English = "Thancred", TraditionalChinese = "桑克瑞德", SimplifiedChinese = "桑克瑞德", MatchSpeaker = true, MatchDialogue = true, Notes = "拂曉幹部" },
            new() { Japanese = "ウリエンジェ", English = "Urianger", TraditionalChinese = "于里昂熱", SimplifiedChinese = "于里昂热", MatchSpeaker = true, MatchDialogue = true, Notes = "拂曉幹部" },
            new() { Japanese = "エスティニ安", English = "Estinien", TraditionalChinese = "埃斯蒂尼安", SimplifiedChinese = "埃斯蒂尼安", MatchSpeaker = true, MatchDialogue = true, Notes = "拂曉幹部" },
            new() { Japanese = "グ・ラハ・ティア", English = "G'raha Tia", TraditionalChinese = "古·拉哈·提亞", SimplifiedChinese = "古·拉哈·提亚", MatchSpeaker = true, MatchDialogue = true, Notes = "拂曉幹部" },
            new() { Japanese = "クルル", English = "Krile", TraditionalChinese = "可露兒", SimplifiedChinese = "可露儿", MatchSpeaker = true, MatchDialogue = true, Notes = "拂曉幹部" },
            new() { Japanese = "リーン", English = "Ryne", TraditionalChinese = "琳", SimplifiedChinese = "琳", MatchSpeaker = true, MatchDialogue = true, Notes = "光之巫女" },
            new() { Japanese = "ゼロ", English = "Zero", TraditionalChinese = "賽羅", SimplifiedChinese = "赛罗", MatchSpeaker = true, MatchDialogue = true, Notes = "半妖" },
            new() { Japanese = "ウクラマト", English = "Wuk Lamat", TraditionalChinese = "烏克拉瑪特", SimplifiedChinese = "乌克拉玛特", MatchSpeaker = true, MatchDialogue = true, Notes = "7.0 核心角色" },
            new() { Japanese = "コーナ", English = "Koana", TraditionalChinese = "科亞納", SimplifiedChinese = "科亚纳", MatchSpeaker = true, MatchDialogue = true, Notes = "7.0 二王子" },
            new() { Japanese = "ゾラージャ", English = "Zoraal Ja", TraditionalChinese = "佐拉爾賈", SimplifiedChinese = "佐拉尔贾", MatchSpeaker = true, MatchDialogue = true, Notes = "7.0 大王子" },
            new() { Japanese = "バクーラジャジャ", English = "Bakool Ja Ja", TraditionalChinese = "巴庫爾賈賈", SimplifiedChinese = "巴库尔贾贾", MatchSpeaker = true, MatchDialogue = true, Notes = "7.0 雙頭連王" },
            new() { Japanese = "グルージャジャ", English = "Gulool Ja Ja", TraditionalChinese = "古魯爾賈賈", SimplifiedChinese = "古鲁尔贾贾", MatchSpeaker = true, MatchDialogue = true, Notes = "7.0 連王" },
            new() { Japanese = "光の戦士", English = "Warrior of Light", TraditionalChinese = "光之戰士", SimplifiedChinese = "光之战士", MatchSpeaker = true, MatchDialogue = true, Notes = "主角稱呼" },
            new() { Japanese = "暁の血盟", English = "Scions of the Seventh Dawn", TraditionalChinese = "拂曉血盟", SimplifiedChinese = "拂晓血盟", MatchSpeaker = true, MatchDialogue = true, Notes = "組織名" },
            new() { Japanese = "エーテル", English = "Aether", TraditionalChinese = "乙太", SimplifiedChinese = "以太", MatchSpeaker = true, MatchDialogue = true, Notes = "核心生命能量" },
            new() { Japanese = "蛮神", English = "Primal", TraditionalChinese = "蠻神", SimplifiedChinese = "蛮神", MatchSpeaker = true, MatchDialogue = true, Notes = "神明概念" },
            new() { Japanese = "エオルゼア", English = "Eorzea", TraditionalChinese = "艾歐澤亞", SimplifiedChinese = "艾欧泽亚", MatchSpeaker = true, MatchDialogue = true, Notes = "主要世界區域" },
            new() { Japanese = "トライヨラ", English = "Tuliyollal", TraditionalChinese = "圖萊尤拉", SimplifiedChinese = "图拉尤拉", MatchSpeaker = true, MatchDialogue = true, Notes = "7.0 主要城鎮" },
            new() { Japanese = "リムサ・ロミンサ", English = "Limsa Lominsa", TraditionalChinese = "利姆薩·羅敏薩", SimplifiedChinese = "利姆萨·罗敏萨", MatchSpeaker = true, MatchDialogue = true, Notes = "海都" },
            new() { Japanese = "グリダニア", English = "Gridania", TraditionalChinese = "格里達尼亞", SimplifiedChinese = "格里达尼亚", MatchSpeaker = true, MatchDialogue = true, Notes = "森都" },
            new() { Japanese = "ウルダハ", English = "Ul'dah", TraditionalChinese = "烏爾達哈", SimplifiedChinese = "乌尔达哈", MatchSpeaker = true, MatchDialogue = true, Notes = "沙都" },
            new() { Japanese = "イシュガルド", English = "Ishgard", TraditionalChinese = "伊修加德", SimplifiedChinese = "伊修加德", MatchSpeaker = true, MatchDialogue = true, Notes = "山都" },
            new() { Japanese = "クガネ", English = "Kugane", TraditionalChinese = "黃金港", SimplifiedChinese = "黄金港", MatchSpeaker = true, MatchDialogue = true, Notes = "東方城邦" },
            new() { Japanese = "クリスタリウム", English = "The Crystarium", TraditionalChinese = "水晶都", SimplifiedChinese = "水晶都", MatchSpeaker = true, MatchDialogue = true, Notes = "第一世界主城" },
            new() { Japanese = "オールド・シャーレアン", English = "Old Sharlayan", TraditionalChinese = "舊薩雷安", SimplifiedChinese = "旧萨雷安", MatchSpeaker = true, MatchDialogue = true, Notes = "知識之都" },
            new() { Japanese = "シャーレアン", English = "Sharlayan", TraditionalChinese = "薩雷安", SimplifiedChinese = "萨雷安", MatchSpeaker = true, MatchDialogue = true, Notes = "知識之都" },
            new() { Japanese = "ラザハン", English = "Radz-at-Han", TraditionalChinese = "拉札罕", SimplifiedChinese = "拉札罕", MatchSpeaker = true, MatchDialogue = true, Notes = "近東城邦" },
            new() { Japanese = "天気予報士", English = "Skywatcher", TraditionalChinese = "天氣預報員", SimplifiedChinese = "天气预报员", MatchSpeaker = true, MatchDialogue = true, Notes = "功能性 NPC" },
            new() { Japanese = "バデロン", English = "Baderon", TraditionalChinese = "巴德隆", SimplifiedChinese = "巴德隆", MatchSpeaker = true, MatchDialogue = true, Notes = "海都溺愛海豚亭老闆" }
        };
    }

    private void LoadCustomGlossaryOnStartup()
    {
        try
        {
            var filePath = CustomGlossaryFilePath;
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var entries = JsonSerializer.Deserialize<List<CustomGlossaryEntry>>(json);
                if (entries != null && config != null)
                {
                    config.CustomGlossaryEntries = entries;
                    log?.Information($"Loaded {entries.Count} custom glossary entries from {filePath}");
                }
            }
            else if (config?.CustomGlossaryEntries != null && config.CustomGlossaryEntries.Count > 0)
            {
                SaveCustomGlossaryToFile(config.CustomGlossaryEntries);
            }
        }
        catch (Exception ex)
        {
            log?.Error(ex, "Failed to load CustomGlossary.json on startup.");
        }
    }

    public List<CustomGlossaryEntry> LoadCustomGlossaryFromFile()
    {
        try
        {
            var filePath = CustomGlossaryFilePath;
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var entries = JsonSerializer.Deserialize<List<CustomGlossaryEntry>>(json);
                if (entries != null)
                {
                    return entries;
                }
            }
            else
            {
                var dataPath = Path.Combine(configDir, "Data", "CustomGlossary.json");
                if (File.Exists(dataPath))
                {
                    var json = File.ReadAllText(dataPath);
                    var entries = JsonSerializer.Deserialize<List<CustomGlossaryEntry>>(json);
                    if (entries != null) return entries;
                }
            }
        }
        catch (Exception ex)
        {
            log?.Error(ex, "Failed to load CustomGlossary.json");
        }

        return config?.CustomGlossaryEntries ?? new();
    }

    public void SaveCustomGlossaryToFile(List<CustomGlossaryEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(configDir);
            var filePath = CustomGlossaryFilePath;
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(entries, options);
            File.WriteAllText(filePath, json, Encoding.UTF8);
            log?.Information($"Saved {entries.Count} custom glossary entries to {filePath}");

            if (config != null)
            {
                config.CustomGlossaryEntries = entries;
                config.Save();
            }
        }
        catch (Exception ex)
        {
            log?.Error(ex, "Failed to save CustomGlossary.json");
        }
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
            log?.Error(ex, $"無法建立目錄：{targetDataDir}");
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
                log?.Error($"下載 {file} 失敗：{err}");
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

            // Ensure en-ch-name.json is present in user targetDataDir
            var userEnPath = Path.Combine(targetDataDir, "en-ch-name.json");
            var bundledEnPath = Path.Combine(bundledDataDir, "en-ch-name.json");
            if (!File.Exists(userEnPath) && File.Exists(bundledEnPath))
            {
                try { File.Copy(bundledEnPath, userEnPath); } catch { }
            }

            LoadGlossaries(configDir);
        }

        if (hasChanges)
        {
            var msg = $"Tataru 文本庫已更新至最新版本！已載入 {nameMap.Count} 筆專有名詞對照（含英文專有名詞 {enNameCount} 筆）、繁中校正 {postReplaceCht.Count} 筆、簡中校正 {postReplaceChs.Count} 筆。";
            log?.Information(msg);
            return (true, true, msg);
        }
        else
        {
            var msg = $"Tataru 文本庫已是最新版本（目前載入 {nameMap.Count} 筆專有名詞對照，含英文專有名詞 {enNameCount} 筆），無須更新。";
            log?.Information(msg);
            return (true, false, msg);
        }
    }
}
