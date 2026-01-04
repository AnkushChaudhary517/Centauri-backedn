using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CentauriSeo.Application.Services;
using CentauriSeo.Infrastructure.Services;

namespace CentauriSeo.Application.Services.ApiClients;

public class ChatGptClient
{
    private readonly HttpClient _client;
    private readonly string _apiKey;
    private readonly ILlmCacheService _cache;

    public ChatGptClient(string apiKey, HttpClient? httpClient = null, ILlmCacheService? cache = null)
    {
        _client = httpClient ?? new HttpClient();
        _apiKey = apiKey;
        _cache = cache!;
    }

    public async Task<string> GetResponseAsync(string prompt)
    {
        var requestObj = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = prompt } }
        };

        var payload = JsonSerializer.Serialize(requestObj);
        var provider = "openai:gpt-4";
        var key = _cache.ComputeRequestKey(payload, provider);

        var cached = await _cache.GetAsync(key);
        if (cached != null) return cached;

        var requestContent = new StringContent(payload, Encoding.UTF8, "application/json");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var response = await _client.PostAsync("https://api.openai.com/v1/chat/completions", requestContent);
        var resultJson = await response.Content.ReadAsStringAsync();

        await _cache.SaveAsync(key, payload, resultJson, provider);

        return resultJson;
    }
}
