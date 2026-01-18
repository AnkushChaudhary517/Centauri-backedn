using Amazon.Runtime.Telemetry.Tracing;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using CentauriSeo.Infrastructure.LlmDtos;
using CentauriSeo.Infrastructure.Logging;
using CentauriSeo.Infrastructure.Services;
using GenerativeAI.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Mscc.GenerativeAI.Types;
using System;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using static CentauriSeo.Core.Models.Utilities.SentenceTaggingPrompts;

namespace CentauriSeo.Infrastructure.LlmClients;

public class GeminiClient
{
    private readonly HttpClient _http;
    private readonly ILlmCacheService _cache;
    private readonly IConfiguration _config;
    private readonly string _apiKey;
    private readonly FileLogger _logger;

    private readonly AiCallTracker _aiCallTracker;
    private readonly IHttpContextAccessor _httpContextAccessor;

    // Use a stable model version unless you are specifically testing 2.0/2.5 previews
    private const string ModelId = "gemini-2.5-flash";
    public GeminiClient(HttpClient http, ILlmCacheService cache, IConfiguration config, AiCallTracker aiCallTracker,
        IHttpContextAccessor httpContextAccessor)
    {
        _http = http;
        _cache = cache;
        _config = config;
        _apiKey = _config["GeminiApiKey"]?.DecodeBase64();
        _logger = new FileLogger();
        _aiCallTracker = aiCallTracker;
        _httpContextAccessor = httpContextAccessor;
    }


    public async Task<string> GetSectionScore(string keyword)
    {
        var systemPrompt = @"You are an SEO research analyst.Use Google Search grounding.Extract only factual competitor data.Output JSON only.";
        string userContent = @"
Locale: ""en-US""

Steps:
1. Search Google for the target keyword.
2. Identify the top 10 organic results (exclude ads, forums, PDFs).
3. Extract H2, H3, H4 headings from each page.
4. Return raw headings without rewriting.
5. Return Intent (The primary intent both for the current keyword and each competitor).
Allowed intent values (choose ONE only per item):
- Informational (Default incase its not clear)
- Commercial
- Transactional
- Navigational
6. Variants
Before scoring, generate a **PK Variant Pool**. Include:
1. **Morphological**: Singular/plural, tenses, noun/verb forms (e.g., ""checker"" -> ""checking"").
2. **Lexical**: Synonyms and common SaaS substitutions (e.g., ""checker"" -> ""tool"", ""platform"").
3. **Search-Derived**: Close semantic variations (e.g., ""AI content checker"" -> ""AI-based quality tool""),People also search for, Titles from top-ranking pages, H2/H3 phrases from top results.
*Constraint: Weight exact matches higher than semantic equivalents.*

Rules:
- Choose the dominant intent, not secondary intent
- Do NOT invent URLs
- Do NOT explain your reasoning
- Do NOT add extra fields
- Output MUST be valid JSON only
- No markdown, no text outside JSON

Output format:
{
  ""keyword"": """",
  ""competitors"": [
    {
      ""url"": """",
      ""headings"": []
    },
    ""intent"": """",
  ],
""intent"": """",
""variants"": []
}

Example:
{
  ""keyword"": ""ai content checker"",
  ""competitors"": [
    {
      ""url"": ""https://example.com/best-running-shoes"",
      ""headings"": [""Top 10 Running Shoes of 2024"" ,""1. Speedster Pro"", ""Features of Speedster Pro"" ]
    }
  ],
""variants"":[""AI content checking"", ""AI-based content checker"", ""v3""]
}
";
        userContent = @$"Target keyword: ""{ keyword}"" " + userContent;
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        var cacheKey = _cache.ComputeRequestKey(userContent, "Gemini:SectionScore");
        var cachedResponse = await _cache.GetAsync(cacheKey);
        if (cachedResponse != null)
        {
            return cachedResponse;
        }
        try
        {
            var responseContent = await ProcessContent(systemPrompt, userContent);
            if (!string.IsNullOrEmpty(responseContent))
            {
                await _cache.SaveAsync(cacheKey, responseContent);
                return responseContent;
            }
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync($"Error occured in get section score :  {ex.Message}:{ex.StackTrace}");
        }
        return string.Empty;
    }
    public async Task<string> GetLevel1InforForAIIndexing(string primaryKeyword, List<Level1Sentence> sentences)
    {
        var req = new {PrimaryKeyword = primaryKeyword, ContentToAnalyze = sentences?.Select(x => new { Text = x.Text, Id = x.Id, InformativeType = x.InformativeType.ToString() }) };
    var responseContent = await ProcessContent(SentenceTaggingPrompts.CentauriLevel1Prompt, JsonSerializer.Serialize(req));
        if (!string.IsNullOrEmpty(responseContent) && !responseContent.Contains("error"))
        {

        }
        return responseContent;
    }
    public async Task<int> GetPlagiarismScore(List<Sentence> sentences)
    {
        try
        {
            var prompt = @"Do a deep search over internet to check for plagiarism and give Unique number of sentences. list of sentences provided in user content . Response should be json with proper unique_sentence_count (int). No other things must be there.if you cant find correct answer then also response format should be same with unique_sentence_count=0";
            var random = new Random();
            var r = Math.Round(sentences.Count * .1);
            List<Sentence> random10 = sentences
                .OrderBy(_ => random.Next())
                .Take((int)r)
                .ToList();
            var responseContent = await ProcessContent(prompt,JsonSerializer.Serialize(random10));

            if(!string.IsNullOrEmpty(responseContent) && !responseContent.Contains("error"))
            {
                using var doc = JsonDocument.Parse(responseContent);
                var uniqueCount =  doc.RootElement
                    .GetProperty("unique_sentence_count")
                    .GetInt32();
                var copiedCount = sentences.Count - uniqueCount;
                var p = copiedCount * 100 / sentences.Count;
                if(p>20)
                {
                    var hugeCacheKey = _cache.ComputeRequestKey(JsonSerializer.Serialize(sentences),"hugeCacheKey");
                    responseContent = await _cache.GetAsync(hugeCacheKey);
                    if(string.IsNullOrEmpty(responseContent))
                    {
                        responseContent = await ProcessContent(prompt, JsonSerializer.Serialize(sentences));
                        if(!string.IsNullOrEmpty(responseContent))
                        {
                            await _cache.SaveAsync(hugeCacheKey, responseContent);
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(responseContent) && !responseContent.Contains("error"))
                    {
                        using var doc2 = JsonDocument.Parse(responseContent); 
                        uniqueCount = doc2.RootElement
                    .GetProperty("unique_sentence_count")
                    .GetInt32();
                        copiedCount = sentences.Count - uniqueCount;
                        p = (int)Math.Ceiling((copiedCount * 100.0) / sentences.Count);
                    }
                }
                return p;
            }
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync($"Error occured in get plagiarism score :  {ex.Message}:{ex.StackTrace}");
        }
        return 0;
    }

    public async Task<IReadOnlyList<GeminiSentenceTag>> TagArticleAsync(string prompt, string xmlContent, string cacheKeySuffix)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        var cacheKey = _cache.ComputeRequestKey(xmlContent, cacheKeySuffix);
        var cachedResponse = await _cache.GetAsync(cacheKey);
        if (cachedResponse != null)
        {
            return JsonSerializer.Deserialize<List<GeminiSentenceTag>>(cachedResponse,options) ?? new List<GeminiSentenceTag>();
        }
        try
        {
            var responseContent = await ProcessContent(prompt, xmlContent);
            var res = JsonSerializer.Deserialize<List<GeminiSentenceTag>>(responseContent, options);
            if (res != null)
            {
                await _cache.SaveAsync(cacheKey, responseContent);
                return res;
            }
        }
        catch(Exception ex)
        {
            await _logger.LogErrorAsync($"Error occured in tagArticleAsync :  {ex.Message}:{ex.StackTrace}");
        }

        return new List<GeminiSentenceTag>();
    }

    public async Task<string> GenerateRecommendationsAsync(string article)
    {
        return await ProcessContent(CentauriSystemPrompts.RecommendationsPrompt, article);
    }

    public async Task<string> ProcessContent(string prompt, string xmlData, bool cachePrompt = false)
    {
        if(cachePrompt)
        {

            var cacheKey = _cache.ComputeRequestKey(prompt, "prompt:caching");
            var cacheName = await _cache.GetAsync(cacheKey);
            if (string.IsNullOrEmpty(cacheName))
            {
                // STEP A: Create Cache
                var cacheResult = await CreateHugeContentCache(prompt, _apiKey);
                cacheName = ParseCacheNameFromJson(cacheResult);
                if (!string.IsNullOrEmpty(cacheName))
                {
                    await _cache.SaveAsync(cacheKey, cacheName);
                }
            }

            var r = await GetAnalysisFromCache(_apiKey, cacheName, xmlData);
            using var doc = JsonDocument.Parse(r);
            return doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString().Replace("```json", "")
                    .Replace("```", "")
                    .Trim();
        }
        else
        {
            var res = await GenerateSentenceTagsDirect(prompt, xmlData);
            return res;

        }
    }

    private async Task<string> GenerateSentenceTagsDirect(string prompt, string xmlContent)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        using var client = new HttpClient() { Timeout = TimeSpan.FromMinutes(10)};

        // Standard GenerateContent request body
        var requestBody = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = prompt } }
            },
            contents = new[]
            {
            new {
                role = "user",
                parts = new[] { new { text = xmlContent } }
            }
        },
            generationConfig = new
            {
                response_mime_type = "application/json" // Forces Gemini to return valid JSON
            }
        };

        var result = await _aiCallTracker.TrackAsync(
            requestBody,
                async () =>
                {
                    var response = await client.PostAsJsonAsync(
                    $"https://generativelanguage.googleapis.com/v1beta/models/{ModelId}:generateContent?key={_apiKey}",
                    requestBody
                    );

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        await _logger.LogErrorAsync($"Error occured in gemini api call : {error}");
                        throw new Exception($"Gemini API Error: {error}");
                    }

                    var res = await response.Content.ReadAsStringAsync();
                   return (res, (JsonSerializer.Deserialize<GeminiResponse>(res, options))?.Usage);

                },
                $"gemini-{ModelId}"
        );
        // Extract only the text content from the Gemini response wrapper
        using var doc = JsonDocument.Parse(result);
        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();


    }

    private static string ParseCacheNameFromJson(string jsonResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            // The API returns the ID in the "name" property
            if (doc.RootElement.TryGetProperty("name", out var nameElement))
            {
                return nameElement.GetString();
            }

            throw new Exception("The response did not contain a 'name' property. Response: " + jsonResponse);
        }
        catch (JsonException ex)
        {
            throw new Exception("Failed to parse Cache Response JSON: " + ex.Message);
        }
    }

    public static async Task<string> GetAnalysisFromCache(string apiKey, string cacheName, string userContent)
    {
        var client = new HttpClient() { Timeout = TimeSpan.FromMinutes(10)};
        var generateRequest = new
        {
            cached_content = cacheName, // Use the ID returned from the creation step
            contents = new[]
            {
            new {
                role = "user",
                parts = new[] { new { text = userContent } }
            }
        }
        };

        var response = await client.PostAsJsonAsync(
            $"https://generativelanguage.googleapis.com/v1beta/models/{ModelId}:generateContent?key={apiKey}",
            generateRequest
        );

        return await response.Content.ReadAsStringAsync();
    }

    public static async Task<string> CreateHugeContentCache(string systemInstruction, string apiKey)
    {
        var client = new HttpClient();

        var cacheRequest = new
        {
            model = $"models/{ModelId}",
            // Instructions are part of the cache
            //system_instruction = new
            //{
            //    parts = new[] { new { text = systemInstruction } }
            //},
            // Put your HUGE XML here to cross the 2,048 token limit
            contents = new[]
            {
            new {
                role = "user",
                parts = new[] { new { text = systemInstruction } }
            }
        },
            ttl = "3600s" // Cache for 1 hour
        };

        var response = await client.PostAsJsonAsync(
            $"https://generativelanguage.googleapis.com/v1beta/cachedContents?key={apiKey}",
            cacheRequest
        );

        var result = await response.Content.ReadAsStringAsync();
        // This returns a JSON containing the "name" (e.g., "cachedContents/abcdef123")
        return result;
    }

    public static async Task<int> GetTokenCount(string apiKey, string prompt, string xmlContent)
    {
        using var client = new HttpClient();

        var countRequest = new
        {
            model = $"models/{ModelId}",
            contents = new[]
            {
            new {
                role = "user",
                parts = new[] { new { text = prompt + "\n" + xmlContent } }
            }
        }
        };

        var response = await client.PostAsJsonAsync(
            $"https://generativelanguage.googleapis.com/v1beta/models/{ModelId}:countTokens?key={apiKey}",
            countRequest
        );
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            return (JsonSerializer.Deserialize<TokenCount>(json,options)).totalTokens;
        }

        return 0;
    }
}

public class TokenDetail
{
    [JsonPropertyName("modality")]
    public string Modality { get; set; }

    [JsonPropertyName("tokenCount")]
    public int TokenCount { get; set; }
}


public class GeminiResponse
{
    [JsonPropertyName("usageMetadata")]
    public GeminiUsage Usage { get; set; }
}
public class GeminiUsage
{
    [JsonPropertyName("promptTokenCount")]
    public int PromptTokenCount { get; set; }

    [JsonPropertyName("candidatesTokenCount")]
    public int CandidatesTokenCount { get; set; }

    [JsonPropertyName("totalTokenCount")]
    public int TotalTokenCount { get; set; }

    [JsonPropertyName("promptTokensDetails")]
    public List<TokenDetail> PromptTokensDetails { get; set; }

    [JsonPropertyName("thoughtsTokenCount")]
    public int ThoughtsTokenCount { get; set; }

    [JsonPropertyName("modelVersion")]
    public string ModelVersion { get; set; }

    [JsonPropertyName("responseId")]
    public string ResponseId { get; set; }
}
