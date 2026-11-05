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
        var raw = string.IsNullOrWhiteSpace(config.LmStudioEndpoint) ? "http://192.168.1.101:1234" : config.LmStudioEndpoint.Trim();
        var trimmed = raw.TrimEnd('/');
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = $"http://{trimmed}";
        }
        if (!trimmed.Contains(':', StringComparison.OrdinalIgnoreCase) || (trimmed.IndexOf(':', 5) == -1 && trimmed.IndexOf(':', 6) == -1))
        {
            trimmed = $"{trimmed}:1234";
        }
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
            var langName = targetLang == TargetLanguage.TraditionalChinese ? "繁體中文（台灣習慣）" : "簡體中文";
            var srcName = config.SourceLang switch
            {
                SourceLanguage.Japanese => "日文",
                SourceLanguage.English => "英文",
                _ => "日文或英文"
            };
            var defaultSystemPrompt = PromptPresets.GetDefaultTemplate(config.DefaultPromptPreset);
            var systemPrompt = string.IsNullOrWhiteSpace(config.CustomSystemPrompt) ? defaultSystemPrompt : config.CustomSystemPrompt;

            systemPrompt = systemPrompt
                .Replace("{targetLang}", langName)
                .Replace("{targetLanguage}", langName)
                .Replace("{sourceLang}", srcName)
                .Replace("{sourceLanguage}", srcName);

            var glossaryRules = glossary.GetCustomGlossaryPrompt(speaker, text, targetLang);
            if (!string.IsNullOrEmpty(glossaryRules))
            {
                systemPrompt += glossaryRules;
            }

            var modelToUse = string.IsNullOrWhiteSpace(config.LmStudioModel) ? "local-model" : config.LmStudioModel.Trim();
            var url = $"{GetBaseUrl()}/chat/completions";
            var temp = Math.Clamp(config.LmStudioTemperature, 0.0f, 1.0f);

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
                    new { role = "user", content = $"請將以下遊戲對白忠實直譯為{langName}（嚴禁接話、嚴禁編造劇情）：\n說話者：{speaker}\n台詞原文：{text}" }
                },
                temperature = temp,
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
                        new { role = "user", content = $"請將以下遊戲對白忠實直譯為{langName}（嚴禁接話、嚴禁編造劇情）：\n說話者：{speaker}\n台詞原文：{text}" }
                    },
                    temperature = temp
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

            bool hasGlossarySpeaker = !string.IsNullOrEmpty(mappedSpeaker) && !string.Equals(mappedSpeaker.Trim(), speaker.Trim(), StringComparison.OrdinalIgnoreCase);
            string resSpeaker = hasGlossarySpeaker ? mappedSpeaker : speaker;
            string resText = text;

            try
            {
                var outputNode = JsonNode.Parse(toParse);
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
                var matchText = System.Text.RegularExpressions.Regex.Match(toParse, @"""text""\s*:\s*""((?:\\.|[^""\\])*)""", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (matchText.Success)
                {
                    resText = matchText.Groups[1].Value.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\"", "\"").Replace("\\\\", "\\");
                }
                var matchName = System.Text.RegularExpressions.Regex.Match(toParse, @"""name""\s*:\s*""((?:\\.|[^""\\])*)""", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (matchName.Success && !hasGlossarySpeaker)
                {
                    resSpeaker = matchName.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                }
            }

            // Fallback: If model returned colon-separated text instead of JSON
            if (resText == toParse && (toParse.Contains("台詞：") || toParse.Contains("台词：") || toParse.Contains("台詞原文：") || toParse.Contains("台词原文：") || toParse.Contains("譯文：") || toParse.Contains("译文：")))
            {
                var matchColon = System.Text.RegularExpressions.Regex.Match(toParse, @"(?:台詞|台词|台詞原文|台词原文|譯文|译文)[：:]\s*(.+)$", System.Text.RegularExpressions.RegexOptions.Multiline);
                if (matchColon.Success)
                {
                    resText = matchColon.Groups[1].Value.Trim();
                }
            }

            if (resText.StartsWith("\"") && resText.EndsWith("\"") && resText.Length >= 2)
            {
                resText = resText.Substring(1, resText.Length - 2);
            }

            // 若目標語言為繁體中文，對 LM Studio 輸出執行簡轉繁轉換（解決本地模型習慣輸出簡體字的問題）
            if (targetLang == TargetLanguage.TraditionalChinese)
            {
                resText = ChineseConverter.ToTraditional(resText);
                if (!string.IsNullOrEmpty(resSpeaker))
                {
                    resSpeaker = ChineseConverter.ToTraditional(resSpeaker);
                }
            }
            else if (targetLang == TargetLanguage.SimplifiedChinese)
            {
                resText = ChineseConverter.ToSimplified(resText);
                if (!string.IsNullOrEmpty(resSpeaker))
                {
                    resSpeaker = ChineseConverter.ToSimplified(resSpeaker);
                }
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
