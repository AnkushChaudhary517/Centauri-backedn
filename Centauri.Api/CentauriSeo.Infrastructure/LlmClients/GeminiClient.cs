using Amazon.Runtime.Telemetry.Tracing;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using CentauriSeo.Infrastructure.LlmDtos;
using CentauriSeo.Infrastructure.Logging;
using CentauriSeo.Infrastructure.Services;
using GenerativeAI;
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
using Tool = Mscc.GenerativeAI.Types.Tool;

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

    public async Task<List<string>> GetCompetitorUrls(string keyword)
    {
        var systemPrompt = @"You are an SEO research analyst.Use Google Search grounding.Extract only factual competitor data.Output JSON only.";
        string userContent = @"
Role: SEO Expert & Content Strategist
Task: Use the Google Search tool to find the top 5 organic results for the keyword.

Constraints:
- Output MUST be valid JSON. 
- No preamble, no explanation, no markdown backticks.
- If a URL cannot be accessed, skip it and move to the next organic result.

Output will be list of string.
";
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        var cacheKey = _cache.ComputeRequestKey(userContent, "Gemini:CompetitorUrls");
        var cachedResponse = await _cache.GetAsync(cacheKey);
        if (cachedResponse != null)
        {
            return JsonSerializer.Deserialize<List<string>>(cachedResponse,options);
        }
        try
        {
            var responseContent = await ProcessContent(systemPrompt, userContent, false, null, true);
            if (!string.IsNullOrEmpty(responseContent))
            {
                await _cache.SaveAsync(cacheKey, responseContent);
                return JsonSerializer.Deserialize<List<string>>(cachedResponse, options);
            }
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync($"Error occured in get getting competitor urls :  {ex.Message}:{ex.StackTrace}");
        }
        return null;
    }
    public async Task<string> GetSectionScore(string keyword)
    {
        //var urls = await GetCompetitorUrls(keyword);
        var systemPrompt = @"You are an SEO research analyst.Use Google Search grounding.Extract only factual competitor data.Output JSON only.";
        string userContent = @"
Role: SEO Expert & Content Strategist
Task: Analyze SERP for the target keyword and generate semantic variants.

Steps:
1. Use the Google Search tool to find the top 5 organic results for the keyword.
2. Extract exact H2 headings from these pages. Do NOT summarize or rewrite them.
3. Identify the Primary Search Intent (Informational, Commercial, Transactional, Navigational).
4. Generate a PK Variant Pool:
   - Morphological: Tenses, plurals, noun/verb forms.
   - Lexical: SaaS-specific synonyms (tool, platform, software).
   - Search-Derived: Variations from ""People also search for"" and competitor titles.

Constraints:
- Output MUST be valid JSON. 
- No preamble, no explanation, no markdown backticks.
- If a URL cannot be accessed, skip it and move to the next organic result.

JSON Schema:
{
  ""keyword"": ""string"",
  ""competitors"": [
    { ""url"": ""string"", ""headings"": [""string""], ""intent"": ""string"" }
  ],
  ""intent"": ""string"",
  ""variants"": [{""text"":""variant text value"", ""variantType"":""Exact|Lexical|Semantic|Morphological|SearchDerived""}]
}

[Strict Rule] : VariantType is an enum with these values (Exact|Lexical|Semantic|Morphological|SearchDerived).
";

        userContent = @$"Target keyword: ""{ keyword}""." + userContent;
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
            var responseContent = await ProcessContent(systemPrompt, userContent, false,null, true);
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
    //public async Task<string> GetLevel1InforForAIIndexing(string primaryKeyword, List<Level1Sentence> sentences)
    //{
    //    var req = new {PrimaryKeyword = primaryKeyword, ContentToAnalyze = sentences?.Select(x => new { Text = x.Text, Id = x.Id, InformativeType = x.InformativeType.ToString() }) };
    //    var start = DateTime.Now;
    //    var responseContent = await ProcessContent(SentenceTaggingPrompts.CentauriLevel1PromptConcise, JsonSerializer.Serialize(req));
    //    var end = DateTime.Now;
    //    return responseContent;
    //}

    public async Task<List<string>> GetLevel1InforForAIIndexing(
    string primaryKeyword,
    List<Level1Sentence> sentences,
    int chunkSize = 25)
    {
        if (sentences == null || sentences.Count == 0)
            return new List<string>();

        var results = new List<string>();

        var start = DateTime.Now;
        var chunks = sentences
            .Select((sentence, index) => new { sentence, index })
            .GroupBy(x => x.index / chunkSize)
            .Select(g => g.Select(x => x.sentence).ToList());

        var tasks = new List<Task<string>>();
        foreach (var chunk in chunks)
        {
            var req = new
            {
                PrimaryKeyword = primaryKeyword,
                ContentToAnalyze = chunk.Select(x => new
                {
                    Text = x.Text,
                    Id = x.Id,
                    InformativeType = x.InformativeType.ToString()
                })
            };

            tasks.Add(ProcessContent(
                SentenceTaggingPrompts.CentauriLevel1PromptConcise,
                JsonSerializer.Serialize(req))); 
        }

        var responses = await Task.WhenAll(tasks);
        results.AddRange(responses);
        var end = DateTime.Now;
        return results;
    }

    public async Task<double> GetPlagiarismScore(List<Sentence> sentences)
    {
        try
        {
            var prompt = @"List of string sentences is provided in the user content.Check properly. Do a deep and thourough search over internet to check for plagiarism and give Unique number of sentences. list of sentences provided in user content . Response should be json with proper unique_sentence_count (int). No other things must be there.if you cant find correct answer then also response format should be same with unique_sentence_count=0";
            var random = new Random();
            var r = Math.Round(sentences.Count * .1);
            List<string> random10 = sentences
                .OrderBy(_ => random.Next())
                .Take((int)r)
                .Select(x => x.Text)
                .ToList();
            var responseContent = await ProcessContent(prompt,JsonSerializer.Serialize(random10),false,null,true);

            if(!string.IsNullOrEmpty(responseContent) && !responseContent.Contains("error"))
            {
                using var doc = JsonDocument.Parse(responseContent);
                var uniqueCount =  doc.RootElement
                    .GetProperty("unique_sentence_count")
                    .GetInt32();
                var copiedCount = random10.Count - uniqueCount;
                var p = (double)(copiedCount * 100) / (double)random10.Count;
                if(p>20)
                {
                    uniqueCount = 0;
                    var start = DateTime.Now;
                    var responseContenListt = await ProcessContentInChunksAsync(prompt, sentences, 50);
                    var end = DateTime.Now;
                    responseContenListt?.ForEach(responseContent =>
                    {
                        if (!string.IsNullOrEmpty(responseContent) && !responseContent.Contains("error"))
                        {
                            using var doc2 = JsonDocument.Parse(responseContent);
                            uniqueCount += doc2.RootElement
                        .GetProperty("unique_sentence_count")
                        .GetInt32();                            
                        }
                    });
                    copiedCount = sentences.Count - uniqueCount;
                    p = Math.Ceiling((copiedCount * 100.0) / (double)sentences.Count);

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

    public async Task<List<string>> ProcessContentInChunksAsync(string prompt, List<Sentence> sentences, int chunkSize = 25, bool enableGoogleSearch=false)
    {
        if (sentences == null || !sentences.Any())
            return new List<string>();

        // Split sentences into chunks
        List<Task<string>> tasks = new List<Task<string>>();
        for (int i = 0; i < sentences.Count; i += chunkSize)
        {
            var chunk = sentences.Skip(i).Take(chunkSize).Select(x => x.Text).ToList();

            // Serialize only the chunk
            var chunkPayload = JsonSerializer.Serialize(chunk);

            // Call the AI
            tasks.Add(ProcessContent(prompt, chunkPayload,false,null,enableGoogleSearch));

        }

        var res = await Task.WhenAll(tasks);

        return res?.ToList();
    }


    public async Task<IReadOnlyList<GeminiSentenceTag>> TagArticleAsync(string prompt, string xmlContent, string cacheKeySuffix)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        var request_article_cache_key = _cache.ComputeRequestKey(xmlContent, "article:content:huge");
        var cached_article_key = await _cache.GetAsync(request_article_cache_key);
        //if(cached_article_key == null)
        //{
        //    cached_article_key = await CreateHugeContentCache(xmlContent, _apiKey);
        //    await _cache.SaveAsync(request_article_cache_key, cached_article_key);
        //}
        var cacheKey = _cache.ComputeRequestKey(xmlContent, cacheKeySuffix);
        var cachedResponse = await _cache.GetAsync(cacheKey);
        if (cachedResponse != null)
        {
            return JsonSerializer.Deserialize<List<GeminiSentenceTag>>(cachedResponse,options) ?? new List<GeminiSentenceTag>();
        }
        try
        {
            var responseContent = await ProcessContent(prompt, xmlContent,false, cached_article_key);
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

    public async Task<string> ProcessContent(string prompt, string xmlData, bool cachePrompt = false, string cachedArticleKey = null, bool enableGoogleSearch=false)
    {
        if(cachePrompt)
        {

            var cacheKey = _cache.ComputeRequestKey(prompt, "prompt:caching");
            var cacheName = await _cache.GetAsync(cacheKey);
            if (string.IsNullOrEmpty(cacheName))
            {
                // STEP A: Create Cache
                cacheName = await CreateHugeContentCache(prompt, _apiKey);
                if (!string.IsNullOrEmpty(cacheName))
                {
                    await _cache.SaveAsync(cacheKey, cacheName);
                }
            }

            return await GetAnalysisFromCache(_apiKey, cacheName, xmlData);
        }
        else
        {
            if (!string.IsNullOrEmpty(cachedArticleKey))
            {
                var r = await GetAnalysisFromCache(_apiKey, cachedArticleKey, prompt);
                return r;
            }
            var res = await GenerateSentenceTagsDirect(prompt, xmlData,enableGoogleSearch);
            return res;

        }
    }

    private async Task<string> GenerateSentenceTagsDirect(string prompt, string xmlContent, bool enableSearch=false)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        using var client = new HttpClient() { Timeout = TimeSpan.FromMinutes(10)};

        int estimatedSentenceCount = (xmlContent.Length / 90) + 1;
        int calculatedMaxTokens = estimatedSentenceCount * 220; // 220 for safety margin
        var toolsList = new List<object>();
        object generationConfig = new();
        // Limit check: Gemini models ki apni limits hoti hain (e.g. 8k for Flash output)
        // calculatedMaxTokens = Math.Clamp(calculatedMaxTokens, 1000, 8192);
        if (enableSearch)
        {
            toolsList.Add(new { google_search = new { } });
            generationConfig = new
            {
                //response_mime_type = "application/json", // Forces Gemini to return valid JSON
                //maxOutputTokens = calculatedMaxTokens,
                temperature = 0.1
            };
        }
        else
        {
            generationConfig = new
            {
                response_mime_type = "application/json", // Forces Gemini to return valid JSON
                //maxOutputTokens = calculatedMaxTokens,
                temperature = 0.1
            };
        }
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
                tools = toolsList.Any() ? toolsList.ToArray() : null,
                
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
                    generationConfig = generationConfig;
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
            .GetString().Replace("```json", "")
                .Replace("```", "")
                .Trim();


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
        var client = new HttpClient() { Timeout = TimeSpan.FromMinutes(10) };

        var generateRequest = new
        {
            cached_content = cacheName,
            contents = new[]
            {
            new {
                role = "user",
                parts = new[] { new { text = userContent } }
            }
        },
            // YEH SECTION ADD KIYA HAI: Markdown hatane ke liye
            generation_config = new
            {
                response_mime_type = "application/json"
            }
        };

        var response = await client.PostAsJsonAsync(
            $"https://generativelanguage.googleapis.com/v1beta/models/{ModelId}:generateContent?key={apiKey}",
            generateRequest
        );

        var jsonResponse = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(jsonResponse);
        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString().Replace("```json", "")
                .Replace("```", "")
                .Trim();
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
        var cacheName = ParseCacheNameFromJson(result);
        // This returns a JSON containing the "name" (e.g., "cachedContents/abcdef123")
        return cacheName;
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
