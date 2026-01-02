using System.Net.Http.Json;

namespace CentauriSeo.Infrastructure.LlmClients;

public class OpenAiClient
{
    private readonly HttpClient _http;

    public OpenAiClient(HttpClient http) => _http = http;

    public async Task<string> CompleteAsync(string prompt)
    {
        var res = await _http.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "gpt-3.5-turbo",
            messages = new[] { new { role = "user", content = prompt } }
        });

        return await res.Content.ReadAsStringAsync();
    }
}
