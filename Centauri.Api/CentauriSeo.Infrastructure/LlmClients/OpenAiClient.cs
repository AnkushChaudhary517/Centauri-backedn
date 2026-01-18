using CentauriSeo.Infrastructure.Services;
using Mscc.GenerativeAI;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CentauriSeo.Infrastructure.LlmClients;

public class OpenAiClient
{
    private readonly HttpClient _http;
    private readonly AiCallTracker _aiCallTracker;

    public OpenAiClient(HttpClient http, AiCallTracker aiCallTracker)
    {
        _http = http;
        _aiCallTracker = aiCallTracker;
    }

    public async Task<string> CompleteAsync(string prompt)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        return await _aiCallTracker.TrackAsync<string>(
            prompt,
                async () =>
                {
                    var res = await _http.PostAsJsonAsync("/v1/chat/completions", new
                    {
                        model = "gpt-3.5-turbo",
                        messages = new[] { new { role = "user", content = prompt } }
                    });
                    var r =  await res.Content.ReadAsStringAsync();
                    return (r, JsonSerializer.Deserialize<OpenAIResponse>(r));

                },
                "openai:gpt-3.5-turbo"
            );
        

        
    }
public class OpenAIUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

public class OpenAIChoice
{
    [JsonPropertyName("text")]
    public string Text { get; set; }

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("finish_reason")]
    public string FinishReason { get; set; }
}

public class OpenAIResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("object")]
    public string Object { get; set; }

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; }

    [JsonPropertyName("usage")]
    public OpenAIUsage Usage { get; set; }

    [JsonPropertyName("choices")]
    public List<OpenAIChoice> Choices { get; set; }
}

}
