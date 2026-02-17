using CentauriSeo.Core.Models.Output;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using CentauriSeo.Infrastructure.LlmDtos;
using CentauriSeo.Infrastructure.Logging;
using CentauriSeo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace CentauriSeo.Infrastructure.LlmClients;

public class GroqClient
{
    private readonly HttpClient _http;
    private readonly ILlmCacheService _cache;
    private readonly string _apiKey;
    private readonly Uri _baseUri;
    private readonly FileLogger _logger;
    private readonly AiCallTracker _aiCallTracker;

    public GroqClient(HttpClient http, ILlmCacheService cache, AiCallTracker aiCallTracker)
    {
        _http = http;
        _cache = cache;
        _apiKey = _http.DefaultRequestHeaders.Authorization?.Parameter ?? string.Empty;
        _baseUri = _http.BaseAddress ?? new Uri("https://api.groq.com");
        _logger = new FileLogger();
        _aiCallTracker = aiCallTracker;
    }

    public async Task<string> UpdateInformativeType(string userContent)
    {
        string endpoint = "https://api.groq.com/openai/v1/chat/completions";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        var requestBody = new
        {
            model = "llama-3.3-70b-versatile",
            messages = new[] {
            new { role = "system", content = $@"
        Please update the userContent where informative Type is wrong and return the data in same format just update the informative type value

Classify as FACT only if the sentence contains specific data, statistics, or universally verifiable truths. Classify as CLAIM if the sentence makes a promotional statement about a product's capabilities, quality, or internal design intent.

Analyze the article and return ONLY a plain JSON ARRAY of objects. 
Do NOT wrap the response in a ""data"" or ""updatedData"" key. 
Do NOT include any conversational text or markdown blocks (```json).
Format(Array of objects):
      [
        {{
          ""InformativeType"": ""Enum_Value"",
          ""Sentence"": ""The actual sentence text""
        }}
      ]

    Allowed Enum Values:
    - Question, Suggestion, Definition, Fact, Statistic, Claim, Uncertain, Opinion, Filler, Prediction

Default value is Uncertain only if there are geninuly no other value possible." 
            
     },
            new { role = "user", content = userContent }
        },
            response_format = new { type = "json_object" },
            temperature = 0 // Lowest temperature for maximum logic 
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(endpoint, content);
            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);
            var data = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return data;
        }
        catch (Exception ex)
        {
            return null;
        }

    } 
    public async Task<List<string>> GetGroqCategorization(List<string> h2Tags)
    {
        string endpoint = "https://api.groq.com/openai/v1/chat/completions";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        var prompt = $@"
    Mapping Rules:
    1. Definition (Concept Clarification)
    2. Process (How-To)
    3. Problem (Pain-Oriented)
    4. Solution (Recommendation)
    5. Analytical (Insight)
    6. Data (Evidence)
    7. Comparison (Evaluates alternatives)
    8. FAQ (Long-tail/edge cases)
    9. Cost (ROI/Pricing)
    10. Implementation (Setup)

    TASK:
    Categorize each of the H2 tags below. 
    Return ONLY a simple JSON array of strings containing the Category Names in the same order as the input.

    H2 TAGS:
    {string.Join("\n", h2Tags)}

    STRICT OUTPUT FORMAT:
    [""""Category1"""", """"Category2"""", """"Category3""""]
    Allowed values: (Definition|Process|Problem|Solution|Analytical|Data|Comparison|FAQ|Cost|Implementation)
    ";

        var requestBody = new
        {
            model = "llama-3.3-70b-versatile",
            messages = new[] {
            new { role = "system", content = "You are a JSON-only SEO classifier. No preamble. No conversational text." },
            new { role = "user", content = prompt }
        },
            response_format = new { type = "json_object" },
            temperature = 0 // Lowest temperature for maximum logic 
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(endpoint, content);
            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);
            var data =  doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            var res =  JsonSerializer.Deserialize<GroqHeadingsResponse>(data);
            return res.categories;
        }
        catch (Exception ex)
        {
            return null;
        }
    }

    // Low-level analyze (kept for compatibility)
    // Sends an OpenAI-compatible chat/completions payload and returns assistant content (machine-parsable JSON expected).
    public async Task<List<PerplexitySentenceTag>> AnalyzeAsync(string payload, string systemRequirement)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        var provider = "groq:analyze";

        // Use a short-lived HttpClient with DNS re-resolve as you had before, but reuse request format below.
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2) // Re-resolve DNS periodically
        };
        using var client = new HttpClient(handler) { BaseAddress = _baseUri };

        if (!string.IsNullOrWhiteSpace(_apiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);


        string rawResponse = null;
        var retryCount = 0;
        var isSuccessful = false;
        var exceptionPlaceholder = "";
        string assistantContent = rawResponse;

        while (retryCount < 2 && !isSuccessful)
        {

            try
            {
                var requestBody = new
                {
                    model = "llama-3.1-8b-instant",
                    messages = new[]
        {
                new { role = "system", content = SentenceTaggingPrompts.GroqRevisedPrompt},
                new { role = "user", content = payload + (!string.IsNullOrEmpty(exceptionPlaceholder)?exceptionPlaceholder:"")
    }
            },
                    temperature = 0.0,
                    max_tokens = 32000
                };
                using var stringContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                rawResponse = await _aiCallTracker.TrackAsync(
                requestBody,
                    async () =>
                    {
                        var res = await client.PostAsync("/openai/v1/chat/completions", stringContent);
                        var r = await res.Content.ReadAsStringAsync();
                        return (r, (JsonSerializer.Deserialize<GroqUsageResponse>(r, options)).Usage);

                    },
                    "groq:llama-3.1-8b-instant"
                );

                using var doc = JsonDocument.Parse(rawResponse);

                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var first = choices[0];
                    if (first.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                    {
                        assistantContent = content.GetString() ?? rawResponse;
                    }
                    else if (first.TryGetProperty("text", out var text))
                    {
                        assistantContent = text.GetString() ?? rawResponse;
                    }
                    else if (first.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var dcontent))
                    {
                        assistantContent = dcontent.GetString() ?? rawResponse;
                    }
                }
                if (!string.IsNullOrWhiteSpace(assistantContent))
                {
                    string json = assistantContent
                        .Replace("```json", "")
                        .Replace("```", "")
                        .Trim();
                    var start = json.IndexOf("[");
                    var end = json.LastIndexOf("]");
                    string inner = "[" + json.Substring(start + 1, end - start - 1) + "]";
                    json = inner;
                    var re = JsonSerializer.Deserialize<List<PerplexitySentenceTag>>(json, options);
                    isSuccessful = true;
                    return re;

                }
            }
            catch (Exception ex)
            {
                exceptionPlaceholder+= ex.Message + " ";
                retryCount++;
                await _logger.LogErrorAsync($"Error occured in AnalyzeAsync : GroqClient : {ex.Message}{ex.StackTrace}");
            }
        }
    
    return null;
    }

    public async Task<IReadOnlyList<PerplexitySentenceTag>> TagArticleAsync(string article)
    {
        var provider = "groq:tagging";
        var key = _cache.ComputeRequestKey(article, provider);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters ={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)}
        };

        var cached = await _cache.GetAsync(key);
        if (cached != null)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<PerplexitySentenceTag>>(cached, options);
                if (parsed != null) return parsed;
            }
            catch { /* ignore parse errors and continue */ }
        }

        try
        {
            var apiResponse = await AnalyzeAsync(article, string.Empty);
            if (apiResponse != null)
            {
                await _cache.SaveAsync(key, JsonSerializer.Serialize(apiResponse));
                return apiResponse;
            }

        }
        catch(Exception ex)
        {
            await _logger.LogErrorAsync($"Error occured in TagArticleAsync : GroqClient : {ex.Message}{ex.StackTrace}");
        }

        return new List<PerplexitySentenceTag>();

    }

    // Tag sentences for Level-1 (returns PerplexitySentenceTag dto for compatibility)
    // Added batching: process sentences in batches and then combine results into a single list.

    public async Task<IReadOnlyList<PerplexitySentenceTag>> TagSentencesAsync(string userContent, string systemRequirement)
    {
        return await TagArticleAsync(userContent);
    }
    public async Task<IReadOnlyList<PerplexitySentenceTag>> TagSentencesAsync(IEnumerable<Sentence> sentences, string systemRequirement)
    {
        var sentenceList = sentences.ToList();
        var payload = JsonSerializer.Serialize(new { sentences = sentenceList.Select(s => new { id = s.Id, text = s.Text }) });
        return await TagArticleAsync(payload);
        //    var provider = "groq:tagging";
        //    var key = _cache.ComputeRequestKey(payload, provider);

        //    var cached = await _cache.GetAsync(key);
        //    if (cached != null)
        //    {
        //        try
        //        {
        //            var parsed = JsonSerializer.Deserialize<List<PerplexitySentenceTag>>(cached, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        //            if (parsed != null && parsed.Count == sentenceList.Count) return parsed;
        //        }
        //        catch { /* ignore parse errors and continue */ }
        //    }

        //    var results = new List<PerplexitySentenceTag>();

        //    // Batch size - tune as needed
        //    const int batchSize = 20;

        //    for (int offset = 0; offset < sentenceList.Count; offset += batchSize)
        //    {
        //        var chunk = sentenceList.Skip(offset).Take(batchSize).ToList();
        //        string apiResponse;

        //        try
        //        {
        //            apiResponse = await AnalyzeAsync(payload, systemRequirement);
        //        }
        //        catch
        //        {
        //            apiResponse = null!;
        //        }

        //        bool parsedChunk = false;

        //        if (!string.IsNullOrWhiteSpace(apiResponse))
        //        {
        //            try
        //            {
        //                var options = new JsonSerializerOptions
        //                {
        //                    PropertyNameCaseInsensitive = true,
        //                    Converters =
        //{
        //    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        //    // or omit JsonNamingPolicy if enum names match exactly
        //}
        //                };
        //                var parsed = JsonSerializer.Deserialize<List<PerplexitySentenceTag>>(apiResponse,options);
        //                if (parsed != null)
        //                {
        //                    results.AddRange(parsed);
        //                    // Map parsed entries to the original sentence IDs by position
        //                    //for (int i = 0; i < parsed.Count; i++)
        //                    //{
        //                    //    var p = parsed[i];
        //                    //    results.Add(new PerplexitySentenceTag
        //                    //    {
        //                    //        SentenceId = chunk[i].Id,
        //                    //        InformativeType = p.InformativeType,
        //                    //        ClaimsCitation = p.ClaimsCitation
        //                    //    });
        //                    //}

        //                    parsedChunk = true;
        //                }
        //            }
        //            catch
        //            {
        //                parsedChunk = false;
        //            }
        //        }

        //        if (!parsedChunk)
        //        {
        //            // Deterministic fallback for this chunk
        //            foreach (var s in chunk)
        //            {
        //                results.Add(new PerplexitySentenceTag
        //                {
        //                    SentenceId = s.Id,
        //                    InformativeType = InformativeTypeDetector.Detect(s.Text),
        //                    ClaimsCitation = CitationDetector.HasCitation(s.Text)
        //                });
        //            }
        //        }
        //    }

        //    // Cache the aggregated result for the full payload
        //    try
        //    {
        //        var finalJson = JsonSerializer.Serialize(results);
        //        await _cache.SaveAsync(key, payload, finalJson, provider);
        //    }
        //    catch
        //    {
        //        // ignore cache errors
        //    }

        //    return results;
    }
}

public class GroqHeadingsResponse
{
    public List<string> categories { get; set; }
}
public class GroqUsageDetails
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }

    [JsonPropertyName("compute_time_ms")]
    public int ComputeTimeMs { get; set; }
}

public class GroqUsageResponse
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; }

    [JsonPropertyName("usage")]
    public GroqUsageDetails Usage { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}
