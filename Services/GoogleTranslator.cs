using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using DalamudTataru.Models;

namespace DalamudTataru.Services;

public class GoogleTranslator : IDisposable
{
    private readonly IPluginLog log;
    private readonly GlossaryManager glossary;
    private readonly HttpClient httpClient;

    public GoogleTranslator(IPluginLog log, GlossaryManager glossary)
    {
        this.log = log;
        this.glossary = glossary;
        this.httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        this.httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public async Task<(string speaker, string text)> TranslateAsync(string speaker, string text, TargetLanguage targetLang, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (speaker, string.Empty);
        }

        try
        {
            var mappedSpeaker = glossary.TranslateSpeaker(speaker, targetLang);
            var tl = targetLang == TargetLanguage.TraditionalChinese ? "zh-TW" : "zh-CN";
            var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=ja&tl={tl}&dt=t&q={Uri.EscapeDataString(text)}";

            var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (mappedSpeaker, $"[Google 錯誤 {response.StatusCode}]");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonNode.Parse(json);

            string rawResult = string.Empty;
            if (parsed is JsonArray rootArr && rootArr.Count > 0 && rootArr[0] is JsonArray sentencesArr)
            {
                foreach (var item in sentencesArr)
                {
                    if (item is JsonArray sentence && sentence.Count > 0)
                    {
                        rawResult += sentence[0]?.ToString();
                    }
                }
            }

            if (string.IsNullOrEmpty(rawResult))
            {
                rawResult = text;
            }

            // Apply Tataru post-translation glossary
            rawResult = glossary.ApplyPostTranslation(rawResult, targetLang);

            return (mappedSpeaker, rawResult);
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to translate via Google Translate.");
            return (speaker, $"[Google 異常: {ex.Message}]");
        }
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
