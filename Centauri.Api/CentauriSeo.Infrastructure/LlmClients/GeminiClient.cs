using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.Runtime.Telemetry.Tracing;
using Azure.Core;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using CentauriSeo.Infrastructure.LlmDtos;
using CentauriSeo.Infrastructure.Logging;
using CentauriSeo.Infrastructure.Services;
using CentauriSeo.Infrastructure.Exceptions;
using GenerativeAI;
using GenerativeAI.Types;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.AIPlatform.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Mscc.GenerativeAI.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using static CentauriSeo.Core.Models.Utilities.SentenceTaggingPrompts;
using GenerateContentRequest = Google.Cloud.AIPlatform.V1.GenerateContentRequest;
using GoogleSearchRetrieval = Google.Cloud.AIPlatform.V1.GoogleSearchRetrieval;
using Tool = Mscc.GenerativeAI.Types.Tool;
using Grpc.Core;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using CentauriSeo.Core.Models.Outputs;

namespace CentauriSeo.Infrastructure.LlmClients;

public class GeminiClient
{
    private readonly HttpClient _http;
    private readonly ILlmCacheService _cache;
    private readonly ILlmCacheManager _cacheManager;
    private readonly IConfiguration _config;
    private readonly string _apiKey;
    private readonly FileLogger _logger;
    private readonly ILlmLogger _llmLogger;

    private readonly AiCallTracker _aiCallTracker;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDynamoDbService _dynamoDbService;

    // Use a stable model version unless you are specifically testing 2.0/2.5 previews
    private const string ModelId = "gemini-2.5-flash";
    //private const string ModelId = "gemini-2.5-pro";
    private readonly string _accessToken;
    private readonly string _modelDefault;
    private readonly string _modelTagging;

    // GCP project & location - used to build resource names for cached content and models
    private readonly string _gcpProject;
    private readonly string _gcpLocation;

    public GeminiClient(HttpClient http, ILlmCacheService cache, IConfiguration config, AiCallTracker aiCallTracker,
        IHttpContextAccessor httpContextAccessor, ILlmCacheManager cacheManager, ILogger<LlmLogger> logger, IDynamoDbService dynamoDbService)
    {
        _http = http;
        _cache = cache;
        _cacheManager = cacheManager;
        _config = config;
        _apiKey = _config["GeminiApiKey"]?.DecodeBase64();
        _accessToken = _config["GeminiAccessToken"]?.DecodeBase64();
        _logger = new FileLogger();
        _llmLogger = new LlmLogger(logger);
        _aiCallTracker = aiCallTracker;
        _httpContextAccessor = httpContextAccessor;

        // GCP project & location
        _gcpProject = _config["Gemini:ProjectId"]
            ?? System.Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
            ?? System.Environment.GetEnvironmentVariable("GCLOUD_PROJECT")
            ?? "gen-lang-client-0445687823";
        _gcpLocation = _config["Gemini:Location"] ?? "asia-south1";
        // Models can be configured in appsettings (fallbacks used if not provided)
        _modelDefault = _config["Gemini:Model:Default"] ?? "gemini-2.5-flash";
        _modelTagging = _config["Gemini:Model:Tagging"] ?? "gemini-2.5-flash";
        if (_gcpProject == "gen-lang-client-0445687823")
        {
            _llmLogger.LogWarning("No Google project configured for Gemini; using fallback project which may not be accessible.");
        }
 
         // Intentionally do not emit informational startup logs here to reduce noise. Errors/warnings are logged above.
         _dynamoDbService = dynamoDbService;
    }
    public async Task<string> GetSectionScore(string keyword)
    {
        const string provider = "Gemini:SectionScore";
        // Removed informational startup log for this operation to reduce log noise
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                throw new LlmValidationException("Keyword cannot be null or empty", provider, new List<string> { "Invalid keyword" });
            }

            var systemPrompt = @"You are an SEO research analyst.Use Google Search grounding.Extract only factual competitor data.Output JSON only.";

            string userContent = @$"
Role: SEO Expert & Content Strategist
Task: Analyze SERP for the target keyword and generate semantic variants.

Steps:
1. Use the Google Search tool to find the top 5 organic results for the keyword. 
   - [Strict Filtering]: Ignore all ""Sponsored"" or ""Ad"" results. Only process organic rankings.
2. Extract exact H2 headings from these pages. Do NOT summarize or rewrite them.
3. Identify the Primary Search Intent (Informational, Commercial, Transactional, Navigational).
4. Generate a PK Variant Pool:
   - Morphological: Tenses, plurals, noun/verb forms.
   - Lexical: SaaS-specific synonyms (tool, platform, software).
   - Search-Derived: Variations from ""People also search for"" and competitor titles.

Constraints:
- Output MUST be valid JSON. 
- No preamble, no explanation, no markdown backticks.
- If a URL cannot be accessed, skip it and move to the next organic result
- Do not repeat the competitors. Currently all competitor urls are the same.
- Never return any competitor which has contentLength = 0.
- url is actual competitor url where article is present with the primary keyword or its variants.

Field Definitions:
- contentLength: This must be the total word count of the main body article/content for that specific organic result. 
- [Critical]: Exclude Ad content entirely. Including word counts from ads will break downstream calculations. If a result is identified as an advertisement, skip it.

JSON Schema:
{{
  ""keyword"": ""string"",
  ""competitors"": [
    {{ 
      ""url"": ""string"", 
      ""headings"": [""string""], 
      ""intent"": ""Informational|Navigational|Transactional|Commercial"", 
      ""contentLength"": ""int"" 
    }}
  ],
  ""intent"": ""Informational|Navigational|Transactional|Commercial"",
  ""variants"": [
    {{
      ""text"":""string"", 
      ""variantType"":""Exact|Lexical|Semantic|Morphological|SearchDerived""
    }}
  ]
}}

[Strict Rule]: VariantType is an enum with these values (Exact|Lexical|Semantic|Morphological|SearchDerived).";
            

            userContent = @$"Target keyword: ""{ keyword}""." + userContent;

            var result = await _cacheManager.ExecuteWithCacheAsync(
                provider,
                userContent,
                () => ProcessContent(systemPrompt, userContent, false, null, true)
            );

            stopwatch.Stop();
            _llmLogger.LogApiCall(provider, "Get Section Score", stopwatch.ElapsedMilliseconds, true);
            return result ?? string.Empty;
        }
        catch (LlmOperationException)
        {
            stopwatch.Stop();
            _llmLogger.LogApiCall(provider, "Get Section Score", stopwatch.ElapsedMilliseconds, false, "LLM Operation failed");
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _llmLogger.LogError($"GetSectionScore failed | Keyword: {keyword}", ex, new Dictionary<string, object>
            {
                { "DurationMs", stopwatch.ElapsedMilliseconds },
                { "Keyword", keyword }
            });
            throw new LlmApiException("Failed to get section score from Gemini", provider, null, ex.Message, ex);
        }
    }

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

    public async Task<IReadOnlyList<GeminiSentenceTag>> TagArticleAsync(string prompt, string xmlContent, string cacheKeySuffix)
    {
        const string provider = "Gemini:TagArticle";
        // Debug log removed to reduce informational noise
        var stopwatch = Stopwatch.StartNew();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        try
        {
            if (string.IsNullOrWhiteSpace(prompt))
                throw new LlmValidationException("Prompt cannot be null or empty", provider, new List<string> { "Invalid prompt" });

            if (string.IsNullOrWhiteSpace(xmlContent))
                throw new LlmValidationException("XML content cannot be null or empty", provider, new List<string> { "Invalid XML content" });

            var responseContent = await _cacheManager.ExecuteWithCacheAsync(
                cacheKeySuffix,
                xmlContent,
                () => ProcessContent(prompt, xmlContent, false, null)
            );

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                _llmLogger.LogWarning("TagArticleAsync returned empty response", new Dictionary<string, object> { { "CacheSuffix", cacheKeySuffix } });
                stopwatch.Stop();
                _llmLogger.LogApiCall(provider, "Tag Article", stopwatch.ElapsedMilliseconds, true);
                return new List<GeminiSentenceTag>();
            }

            try
            {
                var res = JsonSerializer.Deserialize<List<GeminiSentenceTag>>(responseContent, options);
                stopwatch.Stop();
                _llmLogger.LogApiCall(provider, "Tag Article", stopwatch.ElapsedMilliseconds, true);
                return res ?? new List<GeminiSentenceTag>();
            }
            catch (JsonException ex)
            {
                throw new LlmParsingException("Failed to parse Gemini response", provider, responseContent?.Substring(0, Math.Min(200, responseContent.Length)), ex);
            }
        }
        catch (LlmOperationException)
        {
            stopwatch.Stop();
            _llmLogger.LogApiCall(provider, "Tag Article", stopwatch.ElapsedMilliseconds, false, "Operation failed");
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _llmLogger.LogError($"TagArticleAsync failed", ex, new Dictionary<string, object>
            {
                { "CacheSuffix", cacheKeySuffix },
                { "DurationMs", stopwatch.ElapsedMilliseconds }
            });
            throw new LlmApiException("Failed to tag article", provider, null, ex.Message, ex);
        }

    }
    public async Task<string> GenerateRecommendationsAsync(string article)
    {
        var prompt = await GetCachedRecommendationsPromptAsync();

        // Try to create or get a server-side cached-content reference for the large system instruction
        string cachedContentName = null;
        try
        {
            cachedContentName = await CreateOrGetCachedContentAsync(prompt, _modelDefault);
        }
        catch (Exception ex)
        {
            await _logger.LogWarningAsync($"Failed to create/get server-side cached content: {ex.Message}");
        }

        // Build request using default model and reference cached content if present
        var toolsList = new List<object>();
        var reqBody = ConvertToGenerateContentRequest(prompt, article ?? string.Empty, toolsList, _modelDefault, cachedContentName);

        var result = await _aiCallTracker.TrackAsync(
            reqBody,
            async () =>
            {
                var res = await GetGeminiApiResponseAsync(reqBody);
                return (res, res?.UsageMetadata);
            },
            $"gemini-{_modelDefault}"
        );

        if (result == null)
        {
            await _logger.LogErrorAsync($"GEMINI_API_FAILURE: GenerateRecommendations -> null response | Model={_modelDefault} | CachedContent={cachedContentName}");
            return string.Empty;
        }

        var textCandidate = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(textCandidate))
        {
            await _logger.LogErrorAsync($"GEMINI_API_FAILURE: GenerateRecommendations -> empty response text | Model={_modelDefault} | CachedContent={cachedContentName}");
            return string.Empty;
        }
 
        return CleanGeminiJson(result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text);
    }


    private async Task<string> GetCachedRecommendationsPromptAsync()
    {
        var provider = "Gemini:RecommendationsPrompt";
        var cacheKey = _cache.ComputeRequestKey("RecommendationsPrompt", provider);

        try
        {
            var cached = await _cache.GetAsync(cacheKey);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                return cached;
            }
        }
        catch (Exception ex)
        {
            await _logger.LogWarningAsync($"Failed to read cached recommendations prompt: {ex.Message}");
        }

        var prompt = await _dynamoDbService.GetPrompt("RecommendationsPrompt") ?? CentauriSystemPrompts.RecommendationsPrompt;

        try
        {
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                await _cache.SaveAsync(cacheKey, prompt);
            }
        }
        catch (Exception ex)
        {
            await _logger.LogWarningAsync($"Failed to cache recommendations prompt: {ex.Message}");
        }

        return prompt;
    }

    private async Task<string> CreateOrGetCachedContentAsync(string systemInstruction, string modelId = null)
    {
        if (string.IsNullOrWhiteSpace(systemInstruction)) return null;

        var provider = "Gemini:Recommendations:CachedContent";
        var cacheKey = _cache.ComputeRequestKey(systemInstruction, provider);

        try
        {
            var existing = await _dynamoDbService.GetRecommendationCachedContentName();
            if (!string.IsNullOrWhiteSpace(existing))
                return existing;
        }
        catch (Exception ex)
        {
            await _logger.LogWarningAsync($"Error reading cached-content id from cache: {ex.Message}");
        }

        try
        {
            // TTL for the cached content (hours) - configurable, default to 24
            var ttlHours = _config.GetValue<int?>("Gemini:Recommendations:CachedContentTtlHours") ?? _config.GetValue<int>("LlmCache:DurationHours", 24);
            var ttl = TimeSpan.FromHours(ttlHours);

            // Build endpoint URL for creating cached contents
            var url = $"https://{_gcpLocation}-aiplatform.googleapis.com/v1/projects/{_gcpProject}/locations/{_gcpLocation}/cachedContents";

            // Determine model reference
            var modelRef = (modelId ?? _modelDefault);
            if (!modelRef.StartsWith("projects/", StringComparison.OrdinalIgnoreCase) && !modelRef.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            {
                modelRef = BuildModelEndpoint(modelRef);
            }

            // Obtain an access token using ADC
            string token = null;
            try
            {
                var credential = await GoogleCredential.GetApplicationDefaultAsync();
                if (credential.IsCreateScopedRequired)
                {
                    credential = credential.CreateScoped("https://www.googleapis.com/auth/cloud-platform");
                }
                token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync($"GEMINI_CACHED_CONTENT_FAILURE: Failed to obtain ADC token: {ex.Message}\n{ex.StackTrace}");
                token = null;
            }

            using var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(120) };
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            else if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                // Will use API key in query if no token
                url = url + $"?key={_apiKey}";
            }
            else
            {
                // No credentials available
                await _logger.LogWarningAsync("No credentials available to create cached content (no ADC token and no API key). Skipping server-side cache creation.");
                return null;
            }

            var payload = new
            {
                model = modelRef,
                displayName = "Centauri_RecommendationsPrompt",
                systemInstruction = new
                {
                    parts = new[] { new { text = systemInstruction } }
                },
                ttl = $"{(int)ttl.TotalSeconds}s"
            };

            var res = await client.PostAsJsonAsync(url, payload);
            var content = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                await _logger.LogErrorAsync($"GEMINI_CACHED_CONTENT_FAILURE: Failed to create cached content. Status: {res.StatusCode}. Body: {content}");
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                string name = null;
                if (root.TryGetProperty("name", out var nameProp)) name = nameProp.GetString();
                if (string.IsNullOrWhiteSpace(name) && root.TryGetProperty("resourceName", out var rn)) name = rn.GetString();

                if (!string.IsNullOrWhiteSpace(name))
                {

                    try {
                        await _dynamoDbService.SaveRecommendationCachedContentName(name);                    
                    } catch { }
                    return name;
                }
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync($"GEMINI_CACHED_CONTENT_FAILURE: Failed to parse cached content creation response: {ex.Message}\n{ex.StackTrace}");
            }
        }
        catch (Exception ex)
        {
            await _logger.LogWarningAsync($"GEMINI_CACHED_CONTENT_FAILURE: Failed to create/get cached content: {ex.Message}");
        }

        return null;
    }

    public async Task<Google.Cloud.AIPlatform.V1.GenerateContentResponse> GetGeminiApiResponseAsync(GenerateContentRequest requestBody)
    {
        const int maxRetries = 1;
        const int baseDelayMs = 1000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                //var exist = File.Exists("/home/ec2-user/gen-lang-client-0445687823-e31287759ab4.json");
                //if(!exist)
                //{
                //    await _logger.LogErrorAsync($"File does not exist");
                //}
                // 1. Initialize the client using the Builder for specific regions
                var client = await new PredictionServiceClientBuilder
                {
                    Endpoint = "asia-south1-aiplatform.googleapis.com"
                    //   Endpoint = "us-central1-aiplatform.googleapis.com"
                }.BuildAsync();

                // 2. Make the call
                var response = await client.GenerateContentAsync(requestBody);
                return response;
            }
            catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.ResourceExhausted || rpcEx.StatusCode == StatusCode.Unavailable || rpcEx.StatusCode == StatusCode.DeadlineExceeded)
            {
                await _logger.LogErrorAsync($"GEMINI_API_FAILURE: RPC rate limited. Model={requestBody?.Model} Attempt {attempt}/{maxRetries}. Error: {rpcEx.Status.Detail}\n{rpcEx.StackTrace}");
                if (attempt == maxRetries)
                {
                    await _logger.LogErrorAsync($"GEMINI_API_FAILURE: Max retry attempts reached for model={requestBody?.Model} (RPC). Returning null.");
                    return null;
                }

                var backoff = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt - 1));
                await Task.Delay(backoff + TimeSpan.FromMilliseconds(new Random().Next(0, 250)));
                continue;
            }
            catch (Exception ex)
            {
                // Some exceptions may include HTTP 429 info in the message
                var msg = ex.Message ?? string.Empty;
                if (msg.Contains("429") || msg.Contains("Too Many Requests") || msg.Contains("ResourceExhausted"))
                {
                    await _logger.LogErrorAsync($"GEMINI_API_FAILURE: Rate limited (message). Model={requestBody?.Model} Attempt {attempt}/{maxRetries}. Error: {msg}\n{ex.StackTrace}");
                    if (attempt == maxRetries)
                    {
                        await _logger.LogErrorAsync($"GEMINI_API_FAILURE: Max retry attempts reached for model={requestBody?.Model} (message). Returning null.");
                        return null;
                    }
                    var backoff = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt - 1));
                    await Task.Delay(backoff + TimeSpan.FromMilliseconds(new Random().Next(0, 250)));
                    continue;
                }

                await _logger.LogErrorAsync($"GEMINI_API_FAILURE: Error occurred in Gemini API call. Model={requestBody?.Model} Error={ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        return null;
    }

    private string BuildModelEndpoint(string modelId) => $"projects/{_gcpProject}/locations/{_gcpLocation}/publishers/google/models/{modelId}";
    public Google.Cloud.AIPlatform.V1.GenerateContentRequest ConvertToGenerateContentRequest(
        string prompt,
        string xmlContent,
        System.Collections.Generic.IEnumerable<dynamic> toolsList,
        string modelId = "gemini-2.5-flash",
        string cachedContentName = null)
    {
        int approxInputTokens = prompt.Length / 4 + xmlContent.Length / 4;

        // Model limit (Gemini Flash is large, but be conservative)
        const int modelContextLimit = 1_000_000; // safe upper bound (adjust if needed)

        // Keep buffer so response + tools don't overflow
        int remaining = modelContextLimit - approxInputTokens;
        int maxOutput = 10000;
        if (remaining <= 0)
            maxOutput= 1024; // fallback safe minimum

        // ✅ Allocate MORE to output (not /4)
        maxOutput = Math.Min(16384, remaining / 2);

        // ✅ Hard floor for structured output
        if (maxOutput < 2048)
            maxOutput = 2048;
        // Clamp output tokens

        var request = new Google.Cloud.AIPlatform.V1.GenerateContentRequest
        {
            Model = BuildModelEndpoint(modelId)
            //GenerationConfig = new Google.Cloud.AIPlatform.V1.GenerationConfig
            //{
            //    Temperature = 0.3f,              // more deterministic (0.2–0.5 good for structured/XML tasks)
            //    MaxOutputTokens = maxOutput
            //}
            ,
            // Provide explicit generation config so the service knows how many tokens to produce
            GenerationConfig = new Google.Cloud.AIPlatform.V1.GenerationConfig
            {
                // deterministic output for structured tagging
                Temperature = 0.0f,
                //MaxOutputTokens = maxOutput,
                // Request a single candidate by default
                CandidateCount = 1
            }
        };

    

        if (string.IsNullOrWhiteSpace(cachedContentName))
        {
            request.SystemInstruction = new Google.Cloud.AIPlatform.V1.Content
            {
                Parts =
                {
                    new Google.Cloud.AIPlatform.V1.Part { Text = prompt }
                }
            };
        }

        request.Contents.Add(new Google.Cloud.AIPlatform.V1.Content
        {
            Role = "user",
            Parts =
            {
                new Google.Cloud.AIPlatform.V1.Part { Text = xmlContent }
            }
        });

        if (!string.IsNullOrWhiteSpace(cachedContentName))
        {
            try { request.CachedContent = cachedContentName; } catch { }
        }

        if (toolsList != null && toolsList.Count() > 0)
        {
            request.Tools.Add(new Google.Cloud.AIPlatform.V1.Tool
            {
                GoogleSearch = new Google.Cloud.AIPlatform.V1.Tool.Types.GoogleSearch()
            });
        }

        return request;
    }

    public async Task<string> ProcessContent(string prompt, string xmlData, bool cachePrompt = false, string cachedArticleKey = null, bool enableGoogleSearch=false)
    {
        // Simple wrapper - generate using tagging flow for now
        var res = await GenerateSentenceTagsDirect(prompt, xmlData, enableGoogleSearch);
        return res;
    }

    private async Task<string> GenerateSentenceTagsDirect(string prompt, string xmlContent, bool enableSearch = false)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        int estimatedSentenceCount = (string.IsNullOrEmpty(xmlContent) ? 1 : (xmlContent.Length / 90) + 1);
        int calculatedMaxTokens = estimatedSentenceCount * 220; // safety margin
        calculatedMaxTokens = Math.Clamp(calculatedMaxTokens, 256, 1024);

        var toolsList = new List<object>();
        if (enableSearch)
            toolsList.Add(new { google_search = new { } });

        // Add explicit concise output constraints to the system prompt to limit verbosity
        var constraints = "";//$"\n\n---OUTPUT_CONSTRAINTS---\nReturn ONLY valid JSON.\nResponseMimeType: application/json.\nCandidateCount: 1.\nMaxOutputTokens: {calculatedMaxTokens}.\nBe concise: do not add explanations or examples.\n---END_CONSTRAINTS---";
        var effectivePrompt = prompt + constraints;

        var reqBody = ConvertToGenerateContentRequest(effectivePrompt, xmlContent, toolsList, _modelTagging);

        var result = await _aiCallTracker.TrackAsync(
               reqBody,
               async () =>
               {
                   var res = await GetGeminiApiResponseAsync(reqBody);
                   return (res, res?.UsageMetadata);
               },
               $"gemini-{_modelTagging}"
           );

        if (result == null)
        {
            await _logger.LogErrorAsync($"GEMINI_API_FAILURE: GenerateSentenceTagsDirect -> null response | Model={_modelTagging} ");
            return null;
        }

        var tagText = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        if (!string.IsNullOrWhiteSpace(tagText))
        {
            return CleanGeminiJson(tagText);
        }

        // All attempts exhausted
        await _logger.LogErrorAsync($"GEMINI_API_FAILURE: GenerateSentenceTagsDirect -> empty response text  | Model={_modelTagging}");
        return string.Empty;
    }

    public string CleanGeminiJson(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse)) return rawResponse;

        // Remove opening ```json or ```
        if (rawResponse.StartsWith("```"))
        {
            int firstNewLine = rawResponse.IndexOf('\n');
            if (firstNewLine != -1)
            {
                rawResponse = rawResponse.Substring(firstNewLine).Trim();
            }
        }

        // Remove closing ```
        if (rawResponse.EndsWith("```"))
        {
            rawResponse = rawResponse.Substring(0, rawResponse.Length - 3).Trim();
        }

        return rawResponse.Trim();
    }

    /// <summary>
    /// Perform a SERP grounded retrieval for the given keyword and return a structured SectionScoreResponse.
    /// This uses the Google Search tool in Vertex (enabled via ProcessContent) to retrieve top competitors and intents.
    /// </summary>
    public async Task<SectionScoreResponse> GetSectionScoreFromSearchAsync(string keyword)
    {
        const string provider = "Gemini:GetSectionScoreFromSearch";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(keyword))
                throw new LlmValidationException("Keyword cannot be null or empty", provider, new List<string> { "Invalid keyword" });

            // Build a short user instruction that instructs Gemini to use Google Search grounding and return a JSON matching SectionScoreResponse
            var userContent = $@"Use Google Search grounding.

Find up to 5 relevant organic results for the keyword.

Return ONLY original source URLs (no Google redirect links).

For each result:
- url
- intent (Informational|Navigational|Transactional|Commercial)
- headings (only if clearly visible, else empty array)
- contentLength = 0
- variants various semantic/lexical/morphological variations of the keyword derived from the search results (do not return duplicates or variants that are the same as the primary keyword)


Do NOT guess or fabricate data.

Return strict JSON.
JSON Response Schema:
{{
  ""competitors"": [
    {{ 
      ""url"": ""string"", 
      ""headings"": [""string""], 
      ""intent"": ""Informational|Navigational|Transactional|Commercial"", 
      ""contentLength"": ""int"" 
    }}
  ],
  ""variants"": [
    {{
      ""text"":""string"", 
      ""variantType"":""Exact|Lexical|Semantic|Morphological|SearchDerived""
    }}
  ]
}}

Keyword: {keyword}";

            // Use ProcessContent which will call Gemini with Google Search enabled
            var raw = await ProcessContent("Use Google Search grounding.", userContent, false, null, true);

            if (string.IsNullOrWhiteSpace(raw))
            {
                _llmLogger.LogApiCall(provider, "GetSectionScoreFromSearch", stopwatch.ElapsedMilliseconds, false, "Empty response from Gemini");
                return null;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

            try
            {
                var parsed =  JsonSerializer.Deserialize<SectionScoreResponse>(raw, options);
                if (parsed != null)
                {
                    // Normalize keyword
                    parsed.KeyWord = parsed.KeyWord ?? keyword;

                    // Ensure variants are present; if not, generate them via Gemini
                    try
                    {
                        if (parsed.Variants == null || parsed.Variants.Count == 0)
                        {
                            var gen = await GeneratePrimaryKeyVariantsAsync(parsed.KeyWord, enableSearch: true);
                            parsed.Variants = gen ?? new List<Variant2>();
                        }
                        else
                        {
                            // Ensure exact primary present and dedupe
                            if (!parsed.Variants.Any(v => string.Equals(v.Text?.Trim(), parsed.KeyWord?.Trim(), StringComparison.OrdinalIgnoreCase)))
                            {
                                parsed.Variants.Insert(0, new Variant2 { Text = parsed.KeyWord, VariantType = Variant2Type.Exact });
                            }
                            parsed.Variants = parsed.Variants
                                .Where(v => !string.IsNullOrWhiteSpace(v.Text))
                                .GroupBy(v => v.Text.Trim(), StringComparer.OrdinalIgnoreCase)
                                .Select(g => g.First())
                                .ToList();
                        }
                    }
                    catch (Exception ex)
                    {
                        await _logger.LogWarningAsync($"Failed to generate primary keyword variants: {ex.Message}");
                    }

                    stopwatch.Stop();
                    _llmLogger.LogApiCall(provider, "GetSectionScoreFromSearch", stopwatch.ElapsedMilliseconds, true);
                    return parsed;
                }
            }
            catch (JsonException jex)
            {
                // Log parsing error and fallthrough to attempt a best-effort parse
                await _logger.LogErrorAsync($"Failed to deserialize SectionScoreResponse from Gemini: {jex.Message}");
            }

            // If deserialization failed, try to extract the JSON substring heuristically
            //try
            //{
            //    var start = raw.IndexOf('{');
            //    var end = raw.LastIndexOf('}');
            //    if (start >= 0 && end > start)
            //    {
            //        var json = raw.Substring(start, end - start + 1);
            //        var parsed2 = JsonSerializer.Deserialize<SectionScoreResponse>(json, options);
            //        if (parsed2 != null)
            //        {
            //            parsed2.KeyWord = parsed2.KeyWord ?? keyword;

            //            try
            //            {
            //                if (parsed2.Variants == null || parsed2.Variants.Count == 0)
            //                {
            //                    parsed2.Variants = await GeneratePrimaryKeyVariantsAsync(parsed2.KeyWord, enableSearch: true);
            //                }
            //                else
            //                {
            //                    if (!parsed2.Variants.Any(v => string.Equals(v.Text?.Trim(), parsed2.KeyWord?.Trim(), StringComparison.OrdinalIgnoreCase)))
            //                    {
            //                        parsed2.Variants.Insert(0, new Variant2 { Text = parsed2.KeyWord, VariantType = Variant2Type.Exact });
            //                    }
            //                    parsed2.Variants = parsed2.Variants
            //                        .Where(v => !string.IsNullOrWhiteSpace(v.Text))
            //                        .GroupBy(v => v.Text.Trim(), StringComparer.OrdinalIgnoreCase)
            //                        .Select(g => g.First())
            //                        .ToList();
            //                }
            //            }
            //            catch { }

            //            stopwatch.Stop();
            //            _llmLogger.LogApiCall(provider, "GetSectionScoreFromSearch", stopwatch.ElapsedMilliseconds, true);
            //            return parsed2;
            //        }
            //    }
            //}
            //catch { }

            stopwatch.Stop();
            _llmLogger.LogApiCall(provider, "GetSectionScoreFromSearch", stopwatch.ElapsedMilliseconds, false, "Unable to parse Gemini response");
            return null;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _llmLogger.LogApiCall(provider, "GetSectionScoreFromSearch", stopwatch.ElapsedMilliseconds, false, ex.Message);
            throw;
        }
    }

    // Generate primary key variants using Gemini (falls back to Exact variant only)
    private async Task<List<Variant2>> GeneratePrimaryKeyVariantsAsync(string primaryKeyword, bool enableSearch = true)
    {
        if (string.IsNullOrWhiteSpace(primaryKeyword)) return new List<Variant2>();

        var provider = "Gemini:GeneratePrimaryKeyVariants";
        var systemPrompt = @"You are an SEO specialist. Generate a variant pool for the Primary Keyword.
Return ONLY a JSON array. Each item MUST contain:
- text: the variant string
- variantType: one of Exact|Lexical|Semantic|Morphological|SearchDerived

Do NOT include any explanatory text or markdown fences.";

        var userContent = $"PrimaryKeyword: \"{primaryKeyword}\"";

        try
        {
            var raw = await ProcessContent(systemPrompt, userContent, false, null, enableSearch);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<Variant2> { new Variant2 { Text = primaryKeyword, VariantType = Variant2Type.Exact } };
            }

            var cleaned = CleanGeminiJson(raw);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

            List<Variant2> variants = null;
            try
            {
                variants = JsonSerializer.Deserialize<List<Variant2>>(cleaned, options);
            }
            catch
            {
                // Try to extract JSON array substring
                var start = cleaned.IndexOf('[');
                var end = cleaned.LastIndexOf(']');
                if (start >= 0 && end > start)
                {
                    var arr = cleaned.Substring(start, end - start + 1);
                    try { variants = JsonSerializer.Deserialize<List<Variant2>>(arr, options); } catch { variants = null; }
                }
            }

            variants ??= new List<Variant2>();

            // Ensure exact primary present
            if (!variants.Any(v => string.Equals(v.Text?.Trim(), primaryKeyword.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                variants.Insert(0, new Variant2 { Text = primaryKeyword, VariantType = Variant2Type.Exact });
            }

            // Deduplicate by text (case-insensitive)
            var deduped = variants
                .Where(v => !string.IsNullOrWhiteSpace(v.Text))
                .GroupBy(v => v.Text.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            return deduped;
        }
        catch (Exception ex)
        {
            await _logger.LogWarningAsync($"{provider} failed: {ex.Message}");
            return new List<Variant2> { new Variant2 { Text = primaryKeyword, VariantType = Variant2Type.Exact } };
        }
    }

    /// <summary>
    /// Scrape each competitor URL in the provided SectionScoreResponse and populate Headings and ContentLength.
    /// Uses HtmlAgilityPack to parse HTML and extract heading texts and compute word counts.
    /// </summary>
    public async Task<SectionScoreResponse> ScrapeCompetitorUrlsAndPopulateAsync(SectionScoreResponse input)
    {
        if (input == null) return null;

        var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; CentauriBot/1.0; +https://example.com)");

        foreach (var comp in input.Competitors ?? new List<CentauriSeo.Core.Models.Outputs.CompetitorSectionScoreResponse>())
        {
            if (string.IsNullOrWhiteSpace(comp.Url))
            {
                comp.Headings = new List<string>();
                comp.ContentLength = 0;
                continue;
            }

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, comp.Url);
                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseContentRead);

                // If the request followed redirects, use the final Uri as canonical
                var finalUri = resp.RequestMessage?.RequestUri?.ToString();
                if (!string.IsNullOrWhiteSpace(finalUri) && !string.Equals(finalUri, comp.Url, StringComparison.OrdinalIgnoreCase))
                {
                    comp.Url = finalUri;
                }

                var html = await resp.Content.ReadAsStringAsync();

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Remove script/style nodes
                var nuisances = doc.DocumentNode.SelectNodes("//script|//style|//noscript");
                if (nuisances != null)
                {
                    foreach (var n in nuisances) n.Remove();
                }

                // Select only H2 and H3 as requested
                var headingNodes = doc.DocumentNode.SelectNodes("//h2|//h3");
                var headings = new List<string>();
                if (headingNodes != null)
                {
                    foreach (var hn in headingNodes)
                    {
                        var txt = HtmlEntity.DeEntitize(hn.InnerText ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(txt)) headings.Add(txt);
                    }
                }

                // Extract visible text from body
                var bodyNode = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
                var visibleText = HtmlEntity.DeEntitize(bodyNode.InnerText ?? string.Empty);
                var cleansed = Regex.Replace(visibleText, "\\s+", " ").Trim();
                var wordCount = 0;
                if (!string.IsNullOrWhiteSpace(cleansed))
                {
                    wordCount = Regex.Matches(cleansed, "\\b\\w+\\b").Count;
                }

                comp.Headings = headings.Distinct().ToList();
                comp.ContentLength = wordCount;
            }
            catch (Exception ex)
            {
                // On failure, set defaults and log
                comp.Headings = comp.Headings ?? new List<string>();
                comp.ContentLength = 0;
                await _logger.LogWarningAsync($"Failed to scrape competitor URL: {comp.Url} | Error: {ex.Message}");
            }
        }

        return input;
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
