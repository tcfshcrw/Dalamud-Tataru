using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using DalamudTataru.Models;

namespace DalamudTataru.Services;

public class LmStudioTranslator : IDisposable
{
    private readonly Configuration config;
    private readonly GlossaryManager glossary;
    private readonly IPluginLog log;
    private readonly HttpClient httpClient;

    public LmStudioTranslator(Configuration config, GlossaryManager glossary, IPluginLog log)
    {
        this.config = config;
        this.glossary = glossary;
        this.log = log;
        this.httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25) // local LLM inference can take longer
        };
    }

    private string GetBaseUrl()
    {
        var raw = string.IsNullOrWhiteSpace(config.LmStudioEndpoint) ? "http://localhost:1234" : config.LmStudioEndpoint.Trim();
        var trimmed = raw.TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }
        return $"{trimmed}/v1";
    }

    public async Task<List<string>> FetchAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<string>();
        try
        {
            var url = $"{GetBaseUrl()}/models";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(config.LmStudioApiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.LmStudioApiKey.Trim());
            }

            var response = await httpClient.SendAsync(req, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return result;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonNode.Parse(json);
            if (parsed is JsonObject root && root.TryGetPropertyValue("data", out var dataNode) && dataNode is JsonArray dataArr)
            {
                foreach (var item in dataArr)
                {
                    if (item is JsonObject mObj && mObj.TryGetPropertyValue("id", out var idNode))
                    {
                        var id = idNode?.ToString();
                        if (!string.IsNullOrEmpty(id))
                        {
                            result.Add(id);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to fetch models from LM Studio.");
        }

        return result;
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
            var targetLangStr = targetLang == TargetLanguage.TraditionalChinese ? "Traditional Chinese (繁體中文)" : "Simplified Chinese (简体中文)";
            var defaultSystemPrompt = $"You are an expert game localizer for Final Fantasy XIV (FFXIV). Translate the Japanese dialogue into natural, immersive {targetLangStr}. Match character tone and personality. Return ONLY a JSON object with keys 'name' and 'text', without markdown or code fences.";
            var systemPrompt = string.IsNullOrWhiteSpace(config.CustomSystemPrompt) ? defaultSystemPrompt : config.CustomSystemPrompt;

            var modelToUse = string.IsNullOrWhiteSpace(config.LmStudioModel) ? "local-model" : config.LmStudioModel.Trim();
            var url = $"{GetBaseUrl()}/chat/completions";

            var inputPayload = new
            {
                name = speaker,
                text = text
            };

            var requestBody = new
            {
                model = modelToUse,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = JsonSerializer.Serialize(inputPayload) }
                },
                temperature = 0.2,
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "ffxiv_translation",
                        strict = true,
                        schema = new
                        {
                            type = "object",
                            properties = new
                            {
                                name = new { type = "string" },
                                text = new { type = "string" }
                            },
                            required = new[] { "name", "text" }
                        }
                    }
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrWhiteSpace(config.LmStudioApiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.LmStudioApiKey.Trim());
            }
            req.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(req, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorDetail = await response.Content.ReadAsStringAsync(cancellationToken);
                log.Warning($"LM Studio json_schema failed (Status {response.StatusCode}): {errorDetail}. Retrying without response_format...");

                // Retry without response_format for broader compatibility
                var fallbackBody = new
                {
                    model = modelToUse,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = JsonSerializer.Serialize(inputPayload) }
                    },
                    temperature = 0.2
                };

                using var retryReq = new HttpRequestMessage(HttpMethod.Post, url);
                if (!string.IsNullOrWhiteSpace(config.LmStudioApiKey))
                {
                    retryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.LmStudioApiKey.Trim());
                }
                retryReq.Content = new StringContent(JsonSerializer.Serialize(fallbackBody), Encoding.UTF8, "application/json");

                response = await httpClient.SendAsync(retryReq, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    errorDetail = await response.Content.ReadAsStringAsync(cancellationToken);
                    log.Error($"LM Studio retry error (Status {response.StatusCode}): {errorDetail}");
                    return (speaker, $"[LM Studio 錯誤 {response.StatusCode}]");
                }
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            string? candidateText = null;

            try
            {
                var parsed = JsonNode.Parse(responseJson);
                if (parsed is JsonObject rootObj && rootObj.TryGetPropertyValue("choices", out var choicesNode))
                {
                    if (choicesNode is JsonArray choicesArr && choicesArr.Count > 0)
                    {
                        var firstChoice = choicesArr[0] as JsonObject;
                        if (firstChoice != null && firstChoice.TryGetPropertyValue("message", out var msgNode))
                        {
                            if (msgNode is JsonObject msgObj && msgObj.TryGetPropertyValue("content", out var contentNode))
                            {
                                candidateText = contentNode?.ToString();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "Failed to parse LM Studio response payload.");
            }

            if (string.IsNullOrEmpty(candidateText))
            {
                return (speaker, "[LM Studio 未回傳內容]");
            }

            // Extract JSON object substring between '{' and '}'
            string toParse = candidateText.Trim();
            int firstBrace = toParse.IndexOf('{');
            int lastBrace = toParse.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                toParse = toParse.Substring(firstBrace, lastBrace - firstBrace + 1);
            }
            else
            {
                if (toParse.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) toParse = toParse.Substring(7);
                if (toParse.StartsWith("```")) toParse = toParse.Substring(3);
                if (toParse.EndsWith("```")) toParse = toParse.Substring(0, toParse.Length - 3);
                toParse = toParse.Trim();
            }

            string resSpeaker = mappedSpeaker;
            string resText = text;

            try
            {
                var outputNode = JsonNode.Parse(toParse);
                if (outputNode is JsonObject outputObj)
                {
                    if (outputObj.TryGetPropertyValue("name", out var aiNameNode))
                    {
                        var aiName = aiNameNode?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(aiName) && string.IsNullOrEmpty(mappedSpeaker))
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
                else
                {
                    resText = toParse;
                }
            }
            catch
            {
                resText = toParse;
            }

            // Fallback: If JSON parsing didn't extract text, use Regex to extract "text" and "name"
            if (resText == toParse && toParse.Contains("\"text\""))
            {
                var matchText = System.Text.RegularExpressions.Regex.Match(toParse, @"""text""\s*:\s*""([^""]+)""");
                if (matchText.Success)
                {
                    resText = matchText.Groups[1].Value;
                }
                var matchName = System.Text.RegularExpressions.Regex.Match(toParse, @"""name""\s*:\s*""([^""]+)""");
                if (matchName.Success && string.IsNullOrEmpty(mappedSpeaker))
                {
                    resSpeaker = matchName.Groups[1].Value;
                }
            }

            if (resText.StartsWith("\"") && resText.EndsWith("\"") && resText.Length >= 2)
            {
                resText = resText.Substring(1, resText.Length - 2);
            }

            // Post translation correction via Tataru glossary
            resText = glossary.ApplyPostTranslation(resText, targetLang);
            if (!string.IsNullOrEmpty(resSpeaker))
            {
                resSpeaker = glossary.ApplyPostTranslation(resSpeaker, targetLang);
            }

            return (resSpeaker, resText);
        }
        catch (HttpRequestException ex)
        {
            log.Error(ex, "Failed to connect to LM Studio.");
            return (speaker, $"[LM Studio 連線失敗: 請確認伺服器已啟動 ({config.LmStudioEndpoint})]");
        }
        catch (OperationCanceledException)
        {
            return (speaker, "[LM Studio 請求逾時]");
        }
        catch (Exception ex)
        {
            log.Error(ex, "Exception during LM Studio translation.");
            return (speaker, $"[LM Studio 異常: {ex.Message}]");
        }
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
