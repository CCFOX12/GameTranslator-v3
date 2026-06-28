using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GameTranslator.Models;

namespace GameTranslator.Services;

public class TranslationService
{
    private readonly HttpClient _http;
    private readonly ConfigService _config;
    private readonly Dictionary<string, string> _cache = new();
    private const int MaxCacheSize = 200;

    private static readonly Dictionary<string, string> DeepLLangMap = new()
    {
        ["Chinese"] = "ZH", ["English"] = "EN-US", ["Japanese"] = "JA", ["Korean"] = "KO",
    };

    private static readonly Dictionary<string, string> GoogleLangMap = new()
    {
        ["Chinese"] = "zh-CN", ["English"] = "en", ["Japanese"] = "ja", ["Korean"] = "ko",
    };

    public TranslationService(ConfigService config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<string?> TranslateAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var cfg = _config.Config;
        var cacheKey = $"{cfg.ApiType}|{text}|{cfg.TargetLanguage}";
        if (_cache.TryGetValue(cacheKey, out var cached)) return cached;
        string? translated = cfg.ApiType switch
        {
            "DeepL" => await TranslateDeepL(text, cfg),
            "Google" => await TranslateGoogle(text, cfg),
            _ => await TranslateOpenAI(text, cfg),
        };
        if (!string.IsNullOrEmpty(translated))
        {
            if (_cache.Count >= MaxCacheSize) { var first = _cache.Keys.First(); _cache.Remove(first); }
            _cache[cacheKey] = translated;
        }
        return translated;
    }

    private async Task<string?> TranslateDeepL(string text, AppConfig cfg)
    {
        var endpoint = cfg.ApiEndpoint.TrimEnd('/');
        if (!endpoint.EndsWith("/translate")) endpoint += "/v2/translate";
        var targetLang = DeepLLangMap.GetValueOrDefault(cfg.TargetLanguage, "ZH");
        var body = new { text = new[] { text }, target_lang = targetLang };
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", $"DeepL-Auth-Key {cfg.ApiKey}");
        try
        {
            Logger.Info("DeepL: [{0}] -> {1}", text[..Math.Min(text.Length, 25)], targetLang);
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                Logger.Error("DeepL API {0}: {1}", (int)response.StatusCode, content[..Math.Min(content.Length, 200)]);
                return null;
            }
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("translations")[0].GetProperty("text").GetString()?.Trim();
        }
        catch (Exception ex) { Logger.Error("DeepL: {0}", ex.Message); return null; }
    }

    private async Task<string?> TranslateOpenAI(string text, AppConfig cfg)
    {
        var endpoint = NormalizeEndpoint(cfg.ApiEndpoint);
        var requestBody = new
        {
            model = cfg.ModelName,
            messages = new[]
            {
                new { role = "system", content = $"You are a game translator. Translate to {cfg.TargetLanguage}. Keep concise and natural. Return ONLY translation." },
                new { role = "user", content = text }
            },
            temperature = 0.3, max_tokens = 500
        };
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", $"Bearer {cfg.ApiKey}");
        try
        {
            Logger.Info("AI: [{0}] -> {1}", text[..Math.Min(text.Length, 25)], cfg.TargetLanguage);
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                Logger.Error("AI API {0}: {1}", (int)response.StatusCode, body[..Math.Min(body.Length, 200)]);
                return null;
            }
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
        }
        catch (Exception ex) { Logger.Error("AI: {0}", ex.Message); return null; }
    }

    private async Task<string?> TranslateGoogle(string text, AppConfig cfg)
    {
        var targetLang = GoogleLangMap.GetValueOrDefault(cfg.TargetLanguage, "zh-CN");
        var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={targetLang}&dt=t&q={Uri.EscapeDataString(text)}";
        try
        {
            Logger.Info("Google: [{0}] -> {1}", text[..Math.Min(text.Length, 25)], targetLang);
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) { Logger.Error("Google API {0}", (int)response.StatusCode); return null; }
            var raw = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(raw).RootElement[0][0][0].GetString()?.Trim();
        }
        catch (Exception ex) { Logger.Error("Google: {0}", ex.Message); return null; }
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        if (endpoint.EndsWith("/chat/completions")) return endpoint;
        var trimmed = endpoint.TrimEnd('/');
        return trimmed.EndsWith("/v1") ? trimmed + "/chat/completions" : trimmed + "/v1/chat/completions";
    }
}
