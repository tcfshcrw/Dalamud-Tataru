using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using DalamudTataru.Models;

namespace DalamudTataru.Services;

public class YoudaoTranslator : IDisposable
{
    private readonly IPluginLog log;
    private readonly GlossaryManager glossary;
    private readonly HttpClient httpClient;

    private const string R = "fanyideskweb";
    private const string I = "webfanyi";
    private const string S = "client,mysticTime,product";
    private const string L = "1.0.0";
    private const string D = "web";
    private const string U = "fanyi.web";

    private const string DecodeKeyStr = "ydsecret://query/key/B*RGygVywfNBwpmBaZg*WT7SIOUP2T0C9WHMZN39j^DAdaZhAnxvGcCY6VYFwnHl";
    private const string DecodeIvStr = "ydsecret://query/iv/C@lZe2YzHtZ2CYgaXKSVfsb7Y4QWHjITPPZ0nQp87fBeJ!Iv6v^6fvi2WN@bYpJ4";

    private string cookie = string.Empty;
    private string secretKey = string.Empty;
    private long expireTimestamp = 0;

    public YoudaoTranslator(IPluginLog log, GlossaryManager glossary)
    {
        this.log = log;
        this.glossary = glossary;

        var handler = new HttpClientHandler { UseCookies = false };
        this.httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        this.httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    private static byte[] Md5(string input)
    {
        using var md5 = MD5.Create();
        return md5.ComputeHash(Encoding.UTF8.GetBytes(input));
    }

    private static string Md5Hex(string input)
    {
        var bytes = Md5(input);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Decrypt(string base64Data)
    {
        string standard = Regex.Replace(base64Data.Trim(), @"\s+", "").Replace('-', '+').Replace('_', '/');
        switch (standard.Length % 4)
        {
            case 2: standard += "=="; break;
            case 3: standard += "="; break;
        }

        var key = Md5(DecodeKeyStr);
        var iv = Md5(DecodeIvStr);
        var cipherBytes = Convert.FromBase64String(standard);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!string.IsNullOrEmpty(secretKey) && now < expireTimestamp)
        {
            return;
        }

        // 1. Get Cookie from homepage
        var homeRes = await httpClient.GetAsync("https://fanyi.youdao.com/", cancellationToken);
        string userCookie = string.Empty;
        if (homeRes.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var c in cookies)
            {
                var match = Regex.Match(c, @"(OUTFOX_SEARCH_USER_ID=[^;]+)");
                if (match.Success)
                {
                    userCookie = match.Groups[1].Value;
                    break;
                }
            }
        }
        var randomNcoo = (long)(2147483647.0 * Random.Shared.NextDouble());
        this.cookie = $"{userCookie}; OUTFOX_SEARCH_USER_ID_NCOO={randomNcoo}";

        // 2. Fetch secretKey
        long t1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string sign1 = Md5Hex($"client={R}&mysticTime={t1}&product={I}&key=asdjnjfenknafdfsdfsd");
        string keyUrl = $"https://dict.youdao.com/webtranslate/key?keyid=webfanyi-key-getter&sign={sign1}&client={R}&product={I}&appVersion={L}&vendor={D}&pointParam={S}&mysticTime={t1}&keyfrom={U}";

        var keyReq = new HttpRequestMessage(HttpMethod.Get, keyUrl);
        keyReq.Headers.Add("Cookie", this.cookie);
        keyReq.Headers.Add("Origin", "https://fanyi.youdao.com");
        keyReq.Headers.Add("Referer", "https://fanyi.youdao.com/");

        var keyRes = await httpClient.SendAsync(keyReq, cancellationToken);
        var keyJson = await keyRes.Content.ReadAsStringAsync(cancellationToken);
        var keyParsed = JsonNode.Parse(keyJson);
        this.secretKey = keyParsed?["data"]?["secretKey"]?.ToString() ?? string.Empty;
        this.expireTimestamp = now + (3600 * 1000); // 1 hour cache
    }

    public async Task<(string speaker, string text)> TranslateAsync(string speaker, string text, TargetLanguage targetLang, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (speaker, string.Empty);
        }

        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);

            long t2 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string sign2 = Md5Hex($"client={R}&mysticTime={t2}&product={I}&key={this.secretKey}");
            
            // FFXIV terminology: translate speaker name with Tataru glossary
            var mappedSpeaker = glossary.TranslateSpeaker(speaker, targetLang);

            var toLang = targetLang == TargetLanguage.TraditionalChinese ? "zh-CHT" : "zh-CHS";
            var postData = $"i={Uri.EscapeDataString(text)}&from=ja&to={toLang}&dictResult=true&keyid=webfanyi&sign={sign2}&client={R}&product={I}&appVersion={L}&vendor={D}&pointParam={S}&mysticTime={t2}&keyfrom={U}";

            var transReq = new HttpRequestMessage(HttpMethod.Post, "https://dict.youdao.com/webtranslate");
            transReq.Headers.Add("Cookie", this.cookie);
            transReq.Headers.Add("Origin", "https://fanyi.youdao.com");
            transReq.Headers.Add("Referer", "https://fanyi.youdao.com/");
            transReq.Content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded");

            var transRes = await httpClient.SendAsync(transReq, cancellationToken);
            if (!transRes.IsSuccessStatusCode)
            {
                return (mappedSpeaker, $"[有道翻譯錯誤 {transRes.StatusCode}]");
            }

            var encryptedResponse = await transRes.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(encryptedResponse) || encryptedResponse.TrimStart().StartsWith("{"))
            {
                log.Warning($"Youdao returned non-encrypted or empty payload: {encryptedResponse}");
                return (mappedSpeaker, "[有道服務暫時無法連線]");
            }

            string decrypted = Decrypt(encryptedResponse);

            var data = JsonNode.Parse(decrypted);
            var resultArray = data?["translateResult"]?[0]?.AsArray();
            string rawResult = string.Empty;
            if (resultArray != null)
            {
                foreach (var item in resultArray)
                {
                    rawResult += item?["tgt"]?.ToString();
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
            log.Error(ex, "Failed to translate via Youdao.");
            expireTimestamp = 0; // reset authentication on error
            return (speaker, $"[有道異常: {ex.Message}]");
        }
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
