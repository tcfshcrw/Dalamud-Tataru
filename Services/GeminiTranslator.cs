using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using DalamudTataru.Models;

namespace DalamudTataru.Services;

public class GeminiTranslator : IDisposable
{
    private readonly Configuration config;
    private readonly GlossaryManager glossary;
    private readonly YoudaoTranslator youdao;
    private readonly GoogleTranslator google;
    private readonly LmStudioTranslator lmStudio;
    private readonly IPluginLog log;
    private readonly HttpClient httpClient;
    private readonly ConcurrentDictionary<string, (string speaker, string text)> cache = new();

    public GeminiTranslator(Configuration config, GlossaryManager glossary, YoudaoTranslator youdao, GoogleTranslator google, LmStudioTranslator lmStudio, IPluginLog log)
    {
        this.config = config;
        this.glossary = glossary;
        this.youdao = youdao;
        this.google = google;
        this.lmStudio = lmStudio;
        this.log = log;
        this.httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public int CacheCount => cache.Count;

    public int ClearCache()
    {
        var count = cache.Count;
        cache.Clear();
        return count;
    }

    private static bool IsGeminiError(string text)
    {
        return text.StartsWith("[Gemini") || text.StartsWith("[模型") || text.StartsWith("【請先於設定中輸入") || text.StartsWith("[翻譯未回傳結果]") || text.StartsWith("[翻譯請求逾時]") || text.StartsWith("[翻譯異常");
    }

    private static bool IsLmStudioError(string text)
    {
        return text.StartsWith("[LM Studio");
    }

    private static bool IsYoudaoError(string text)
    {
        return text.StartsWith("[有道異常") || text.StartsWith("[有道翻譯錯誤") || text.StartsWith("[有道服務");
    }

    private static bool IsGoogleError(string text)
    {
        return text.StartsWith("[Google 錯誤") || text.StartsWith("[Google 異常");
    }

    private async Task<(string speaker, string text)> TranslateWithLmStudioPrimaryAsync(string speaker, string text, CancellationToken cancellationToken)
    {
        try
        {
            var (lmSpeaker, lmText) = await lmStudio.TranslateAsync(speaker, text, config.TargetLang, cancellationToken);
            if (!IsLmStudioError(lmText))
            {
                return (lmSpeaker, lmText);
            }
            log.Warning($"LM Studio primary failed: {lmText}");
        }
        catch (Exception ex)
        {
            log.Warning(ex, "LM Studio primary failed with exception.");
        }

        if (!config.EnableFallback)
        {
            return (speaker, "[LM Studio 翻譯失敗]");
        }

        // 1st fallback: Youdao (100% free public web service, zero cost)
        log.Information("Attempting free Youdao fallback from LM Studio.");
        try
        {
            var (ydSpeaker, ydText) = await youdao.TranslateAsync(speaker, text, config.TargetLang, cancellationToken);
            if (!IsYoudaoError(ydText))
            {
                return (ydSpeaker, $"[有道備用] {ydText}");
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Youdao fallback failed.");
        }

        // 2nd fallback: Google Translate (100% free public web service, zero cost)
        log.Information("Attempting free Google fallback from LM Studio.");
        try
        {
            var (ggSpeaker, ggText) = await google.TranslateAsync(speaker, text, config.TargetLang, cancellationToken);
            if (!IsGoogleError(ggText))
            {
                return (ggSpeaker, $"[谷歌備用] {ggText}");
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Google fallback failed.");
        }

        return (speaker, "[LM Studio 與免費備用防線皆翻譯失敗]");
    }

    private async Task<(string speaker, string text)> TranslateWithGeminiPrimaryAsync(string speaker, string text, CancellationToken cancellationToken)
    {
        var result = await TranslateWithGeminiCoreAsync(speaker, text, cancellationToken);
        if (!IsGeminiError(result.text))
        {
            return result;
        }

        if (!config.EnableFallback)
        {
            return result;
        }

        log.Information($"Gemini failed ({result.text}), attempting Youdao fallback.");
        try
        {
            var (ydSpeaker, ydText) = await youdao.TranslateAsync(speaker, text, config.TargetLang, cancellationToken);
            if (!IsYoudaoError(ydText))
            {
                return (ydSpeaker, $"[有道備用] {ydText}");
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Youdao fallback failed.");
        }

        log.Information("Youdao fallback failed, attempting Google fallback.");
        try
        {
            var (ggSpeaker, ggText) = await google.TranslateAsync(speaker, text, config.TargetLang, cancellationToken);
            if (!IsGoogleError(ggText))
            {
                return (ggSpeaker, $"[谷歌備用] {ggText}");
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Google fallback failed.");
        }

        return (speaker, $"[全線翻譯異常 - 原Gemini: {result.text}]");
    }

    private async Task<(string speaker, string text)> TranslateWithYoudaoPrimaryAsync(string speaker, string text, CancellationToken cancellationToken)
    {
        try
        {
            var (ydSpeaker, ydText) = await youdao.TranslateAsync(speaker, text, config.TargetLang, cancellationToken);
            if (!IsYoudaoError(ydText))
            {
                return (ydSpeaker, ydText);
            }
            log.Warning($"Youdao primary failed: {ydText}");
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Youdao primary failed with exception.");
        }

        if (!config.EnableFallback)
        {
            return (speaker, "[有道翻譯失敗]");
        }

        // Fallback: Google Translate (100% free public web service, zero cost)
        log.Information("Attempting free Google fallback.");
        try
        {
            var (ggSpeaker, ggText) = await google.TranslateAsync(speaker, text, config.TargetLang, cancellationToken);
            if (!IsGoogleError(ggText))
            {
                return (ggSpeaker, $"[谷歌備用] {ggText}");
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Google fallback failed.");
        }

        return (speaker, "[有道與 Google 免費備用皆失敗]");
    }

    private async Task<(string speaker, string text)> TranslateWithGooglePrimaryAsync(string speaker, string text, CancellationToken cancellationToken)
    {
        try
        {
            var (ggSpeaker, ggText) = await google.TranslateAsync(speaker, text, config.TargetLang, cancellationToken);
            if (!IsGoogleError(ggText))
            {
                return (ggSpeaker, ggText);
            }
            log.Warning($"Google primary failed: {ggText}");
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Google primary failed with exception.");
        }

        if (!config.EnableFallback)
        {
            return (speaker, "[Google 翻譯失敗]");
        }

        // Fallback: Youdao (100% free public web service, zero cost)
        log.Information("Attempting free Youdao fallback.");
        try
        {
            var (ydSpeaker, ydText) = await youdao.TranslateAsync(speaker, text, config.TargetLang, cancellationToken);
            if (!IsYoudaoError(ydText))
            {
                return (ydSpeaker, $"[有道備用] {ydText}");
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Youdao fallback failed.");
        }

        return (speaker, "[Google 與有道免費備用皆失敗]");
    }

    public async Task<List<string>> FetchAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(config.GeminiApiKey))
        {
            return result;
        }

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={config.GeminiApiKey.Trim()}";
            var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return result;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonNode.Parse(json);
            if (parsed is JsonObject root && root.TryGetPropertyValue("models", out var modelsNode) && modelsNode is JsonArray modelsArray)
            {
                foreach (var m in modelsArray)
                {
                    if (m is JsonObject mObj && mObj.TryGetPropertyValue("name", out var nameNode))
                    {
                        var name = nameNode?.ToString();
                        bool supportsGen = false;
                        if (mObj.TryGetPropertyValue("supportedGenerationMethods", out var methodsNode) && methodsNode is JsonArray methodsArr)
                        {
                            foreach (var method in methodsArr)
                            {
                                if (method?.ToString() == "generateContent")
                                {
                                    supportsGen = true;
                                    break;
                                }
                            }
                        }

                        if (supportsGen && !string.IsNullOrEmpty(name))
                        {
                            if (name.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                            {
                                name = name.Substring(7);
                            }
                            if (name.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
                            {
                                result.Add(name);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to fetch Gemini models list.");
        }

        return result;
    }

    public async Task<(string speaker, string text)> TranslateWithGeminiCoreAsync(string speaker, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.GeminiApiKey))
        {
            return (speaker, "【請先於設定中輸入 Gemini API Key】");
        }

        try
        {
            // First check if speaker is in our local glossary
            var mappedSpeaker = glossary.TranslateSpeaker(speaker, config.TargetLang);

            var targetLangStr = config.TargetLang == TargetLanguage.TraditionalChinese ? "Traditional Chinese (繁體中文)" : "Simplified Chinese (简体中文)";
            var sourceLangStr = config.SourceLang switch
            {
                SourceLanguage.Japanese => "Japanese",
                SourceLanguage.English => "English",
                _ => "Japanese or English"
            };
            var defaultSystemPrompt = PromptPresets.GetDefaultTemplate(config.DefaultPromptPreset);
            var systemPrompt = string.IsNullOrWhiteSpace(config.CustomSystemPrompt) ? defaultSystemPrompt : config.CustomSystemPrompt;

            systemPrompt = systemPrompt
                .Replace("{targetLang}", targetLangStr)
                .Replace("{targetLanguage}", targetLangStr)
                .Replace("{sourceLang}", sourceLangStr)
                .Replace("{sourceLanguage}", sourceLangStr);

            var glossaryRules = glossary.GetCustomGlossaryPrompt(speaker, text, config.TargetLang);
            if (!string.IsNullOrEmpty(glossaryRules))
            {
                systemPrompt += glossaryRules;
            }

            var model = string.IsNullOrWhiteSpace(config.GeminiModel) ? "gemini-3.8-flash" : config.GeminiModel.Trim();
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={config.GeminiApiKey.Trim()}";

            var inputPayload = new
            {
                name = speaker,
                text = text
            };

            var requestBody = new
            {
                system_instruction = new
                {
                    parts = new[] { new { text = systemPrompt } }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = $"請將以下遊戲對白忠實直譯為{targetLangStr}（嚴禁接話、嚴禁編造劇情）：\n說話者：{speaker}\n台詞原文：{text}" } }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2,
                    response_mime_type = "application/json"
                }
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(url, jsonContent, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorDetail = await response.Content.ReadAsStringAsync(cancellationToken);
                log.Error($"Gemini API error (Status {response.StatusCode}): {errorDetail}");
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return (speaker, $"[模型 '{model}' 不存在或已被 Google 停用 (404)]");
                }
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    return (speaker, "[Gemini API 請求過於頻繁或額度已達上限 (429)]");
                }
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return (speaker, "[Gemini API Key 無效或無存取權限 (401/403)]");
                }
                return (speaker, $"[Gemini 錯誤 {response.StatusCode}]");
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            string? candidateText = null;

            try
            {
                var parsed = JsonNode.Parse(responseJson);
                if (parsed is JsonObject rootObj && rootObj.TryGetPropertyValue("candidates", out var candidatesNode))
                {
                    if (candidatesNode is JsonArray candidatesArr && candidatesArr.Count > 0)
                    {
                        var firstCandidate = candidatesArr[0] as JsonObject;
                        if (firstCandidate != null && firstCandidate.TryGetPropertyValue("content", out var contentNode))
                        {
                            if (contentNode is JsonObject contentObj && contentObj.TryGetPropertyValue("parts", out var partsNode))
                            {
                                if (partsNode is JsonArray partsArr && partsArr.Count > 0)
                                {
                                    var firstPart = partsArr[0] as JsonObject;
                                    if (firstPart != null && firstPart.TryGetPropertyValue("text", out var textNode))
                                    {
                                        candidateText = textNode?.ToString();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Failed to parse Gemini response payload.");
            }

            if (string.IsNullOrEmpty(candidateText))
            {
                return (speaker, "[翻譯未回傳結果]");
            }

            // Clean markdown block if present
            // Extract JSON object substring between '{' and '}'
            string cleanJson = candidateText.Trim();
            int firstBrace = cleanJson.IndexOf('{');
            int lastBrace = cleanJson.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                cleanJson = cleanJson.Substring(firstBrace, lastBrace - firstBrace + 1);
            }
            else
            {
                if (cleanJson.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) cleanJson = cleanJson.Substring(7);
                if (cleanJson.StartsWith("```")) cleanJson = cleanJson.Substring(3);
                if (cleanJson.EndsWith("```")) cleanJson = cleanJson.Substring(0, cleanJson.Length - 3);
                cleanJson = cleanJson.Trim();
            }

            bool hasGlossarySpeaker = !string.IsNullOrEmpty(mappedSpeaker) && !string.Equals(mappedSpeaker.Trim(), speaker.Trim(), StringComparison.OrdinalIgnoreCase);
            string resSpeaker = hasGlossarySpeaker ? mappedSpeaker : speaker;
            string resText = text;

            try
            {
                var outputNode = JsonNode.Parse(cleanJson);
                if (outputNode is JsonObject outputObj)
                {
                    if (outputObj.TryGetPropertyValue("name", out var aiNameNode))
                    {
                        var aiName = aiNameNode?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(aiName) && !hasGlossarySpeaker)
                        {
                            resSpeaker = aiName;
                        }
                    }
                    if (outputObj.TryGetPropertyValue("text", out var aiTextNode))
                    {
                        var aiText = aiTextNode?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(aiText))
                        {
                            resText = aiText;
                        }
                    }
                }
                else if (outputNode is JsonArray outputArr && outputArr.Count > 0)
                {
                    if (outputArr[0] is JsonObject firstObj)
                    {
                        if (firstObj.TryGetPropertyValue("name", out var aiNameNode))
                        {
                            var aiName = aiNameNode?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(aiName) && !hasGlossarySpeaker)
                            {
                                resSpeaker = aiName;
                            }
                        }
                        if (firstObj.TryGetPropertyValue("text", out var aiTextNode))
                        {
                            var aiText = aiTextNode?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(aiText))
                            {
                                resText = aiText;
                            }
                        }
                    }
                }
                else
                {
                    resText = cleanJson;
                }
            }
            catch
            {
                resText = cleanJson;
            }

            // Fallback: If JSON parsing didn't extract text, use Regex to extract "text" and "name"
            if (resText == cleanJson && cleanJson.Contains("\"text\""))
            {
                var matchText = System.Text.RegularExpressions.Regex.Match(cleanJson, @"""text""\s*:\s*""((?:\\.|[^""\\])*)""", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (matchText.Success)
                {
                    resText = matchText.Groups[1].Value.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\"", "\"").Replace("\\\\", "\\");
                }
                var matchName = System.Text.RegularExpressions.Regex.Match(cleanJson, @"""name""\s*:\s*""((?:\\.|[^""\\])*)""", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (matchName.Success && !hasGlossarySpeaker)
                {
                    resSpeaker = matchName.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                }
            }

            if (resText.StartsWith("\"") && resText.EndsWith("\"") && resText.Length >= 2)
            {
                resText = resText.Substring(1, resText.Length - 2);
            }

            // 若目標語言為繁體中文，對輸出執行簡轉繁轉換（避免模型偶爾回傳簡體字）
            if (config.TargetLang == TargetLanguage.TraditionalChinese)
            {
                resText = ChineseConverter.ToTraditional(resText);
                if (!string.IsNullOrEmpty(resSpeaker))
                {
                    resSpeaker = ChineseConverter.ToTraditional(resSpeaker);
                }
            }
            else if (config.TargetLang == TargetLanguage.SimplifiedChinese)
            {
                resText = ChineseConverter.ToSimplified(resText);
                if (!string.IsNullOrEmpty(resSpeaker))
                {
                    resSpeaker = ChineseConverter.ToSimplified(resSpeaker);
                }
            }

            // Post translation correction via Tataru glossary
            resText = glossary.ApplyPostTranslation(resText, config.TargetLang);
            if (!string.IsNullOrEmpty(resSpeaker))
            {
                resSpeaker = glossary.ApplyPostTranslation(resSpeaker, config.TargetLang);
            }

            return (resSpeaker, resText);
        }
        catch (OperationCanceledException)
        {
            return (speaker, "[翻譯請求逾時]");
        }
        catch (Exception ex)
        {
            log.Error(ex, "Exception during Gemini translation");
            return (speaker, $"[Gemini 異常: {ex.Message}]");
        }
    }

    public async Task<(string speaker, string text)> TranslateAsync(string speaker, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (speaker, string.Empty);
        }

        var (maskedSpeaker, maskedText) = PlayerNameHelper.Mask(speaker, text, config);
        maskedText = glossary.ApplyPreTranslation(maskedText, config.TargetLang);

        var cacheKey = $"{config.PrimaryEngine}::{config.SourceLang}::{config.TargetLang}::{speaker}::{text}";
        if (cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        (string s, string t) result;
        switch (config.PrimaryEngine)
        {
            case TranslationEngine.LmStudio:
                result = await TranslateWithLmStudioPrimaryAsync(maskedSpeaker, maskedText, cancellationToken);
                break;
            case TranslationEngine.Youdao:
                result = await TranslateWithYoudaoPrimaryAsync(maskedSpeaker, maskedText, cancellationToken);
                break;
            case TranslationEngine.Google:
                result = await TranslateWithGooglePrimaryAsync(maskedSpeaker, maskedText, cancellationToken);
                break;
            case TranslationEngine.Gemini:
            default:
                result = await TranslateWithGeminiPrimaryAsync(maskedSpeaker, maskedText, cancellationToken);
                break;
        }

        result = (PlayerNameHelper.Unmask(result.s, config), PlayerNameHelper.Unmask(result.t, config));

        if (!IsAnyTranslationError(result.t))
        {
            cache[cacheKey] = result;
        }
        return result;
    }

    public static bool IsAnyTranslationError(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        return IsGeminiError(text) ||
               IsLmStudioError(text) ||
               IsYoudaoError(text) ||
               IsGoogleError(text) ||
               text.StartsWith("[全線翻譯異常") ||
               text.StartsWith("[有道翻譯失敗]") ||
               text.StartsWith("[Google 翻譯失敗]") ||
               text.StartsWith("[有道與 Google") ||
               text.StartsWith("[Google 與有道") ||
               text.StartsWith("[所有翻譯管道皆失敗]");
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
