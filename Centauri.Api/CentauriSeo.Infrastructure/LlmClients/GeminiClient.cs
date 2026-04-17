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
    private const string Endpoint = $"projects/gen-lang-client-0445687823/locations/us-central1/publishers/google/models/{ModelId}";
    private const string _apiURL =$"https://us-central1-aiplatform.googleapis.com/v1/{Endpoint}:generateContent";
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

        _llmLogger.LogInfo("GeminiClient initialized successfully");
        _dynamoDbService = dynamoDbService;
    }

    //    public async Task<List<string>> GetCompetitorUrls(string keyword)
    //    {
    //        var systemPrompt = @"You are an SEO research analyst.Use Google Search grounding.Extract only factual competitor data.Output JSON only.";
    //        string userContent = @"
    //Role: SEO Expert & Content Strategist
    //Task: Use the Google Search tool to find the top 5 organic results for the keyword.

    //Constraints:
    //- Output MUST be valid JSON. 
    //- No preamble, no explanation, no markdown backticks.
    //- If a URL cannot be accessed, skip it and move to the next organic result.

    //Output will be list of string.
    //";
    //        var options = new JsonSerializerOptions
    //        {
    //            PropertyNameCaseInsensitive = true,
    //            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    //        };
    //        var cacheKey = _cache.ComputeRequestKey(userContent, "Gemini:CompetitorUrls");
    //        var cachedResponse = await _cache.GetAsync(cacheKey);
    //        if (cachedResponse != null)
    //        {
    //            return JsonSerializer.Deserialize<List<string>>(cachedResponse,options);
    //        }
    //        try
    //        {
    //            var responseContent = await ProcessContent(systemPrompt, userContent, false, null, true);
    //            if (!string.IsNullOrEmpty(responseContent))
    //            {
    //                await _cache.SaveAsync(cacheKey, responseContent);
    //                return JsonSerializer.Deserialize<List<string>>(cachedResponse, options);
    //            }
    //        }
    //        catch (Exception ex)
    //        {
    //            await _logger.LogErrorAsync($"Error occured in get getting competitor urls :  {ex.Message}:{ex.StackTrace}");
    //        }
    //        return null;
    //    }
    public async Task<string> GetSectionScore(string keyword)
    {
        const string provider = "Gemini:SectionScore";
        _llmLogger.LogInfo($"GetSectionScore started | Keyword: {keyword}");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                throw new LlmValidationException("Keyword cannot be null or empty", provider, new List<string> { "Invalid keyword" });
            }

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
    { ""url"": ""string"", ""headings"": [""string""], ""intent"": ""Informational|Navigational|Transactional|Commercial"" }
  ],
  ""intent"": ""Informational|Navigational|Transactional|Commercial"",
  ""variants"": [{""text"":""variant text value"", ""variantType"":""Exact|Lexical|Semantic|Morphological|SearchDerived""}]
}

[Strict Rule] : VariantType is an enum with these values (Exact|Lexical|Semantic|Morphological|SearchDerived).
";

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
        const string provider = "Gemini:TagArticle";
        _llmLogger.LogDebug($"TagArticleAsync started | CacheSuffix: {cacheKeySuffix}");
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
        var prompt = await _dynamoDbService.GetPrompt("RecommendationsPrompt") ?? CentauriSystemPrompts.RecommendationsPrompt;
        return await ProcessContent(prompt, article, false);
    }

    public async Task<string> ProcessContent(string prompt, string xmlData, bool cachePrompt = false, string cachedArticleKey = null, bool enableGoogleSearch=false)
    {
        //if(cachePrompt)
        //{

            //var cacheKey = _cache.ComputeRequestKey(prompt, "prompt:caching");
            //var cacheName = await _cache.GetAsync(cacheKey);
            //if (string.IsNullOrEmpty(cacheName))
            //{
            //    // STEP A: Create Cache
            //    cacheName = await CreateHugeContentCache(prompt, _apiKey);
            //    if (!string.IsNullOrEmpty(cacheName))
            //    {
            //        await _cache.SaveAsync(cacheKey, cacheName);
            //    }
            //}

            //return await GetAnalysisFromCache(_apiKey, cacheName, xmlData);
        //}
        //else
        //{
            //if (!string.IsNullOrEmpty(cachedArticleKey))
            //{
            //    var r = await GetAnalysisFromCache(_apiKey, cachedArticleKey, prompt);
            //    return r;
            //}
            var res = await GenerateSentenceTagsDirect(prompt, xmlData,enableGoogleSearch);
            return res;

        //}
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
        var reqBody = ConvertToGenerateContentRequest(prompt,xmlContent,toolsList);
        //    // Standard GenerateContent request body
        //    var requestBody = new 
        //    {
        //        system_instruction = new
        //        {
        //            parts = new[] { new { text = prompt } }
        //        },
        //        contents = new[]
        //        {
        //    new {
        //        role = "user",
        //        parts = new[] { new { text = xmlContent } }
        //        }
        //    },
        //        tools = toolsList.Any() ? toolsList.ToArray() : null,
                
        //    };

        var result = await _aiCallTracker.TrackAsync(
            reqBody,
                async () =>
                {
                    //var response = await client.PostAsJsonAsync(
                    //$"https://generativelanguage.googleapis.com/v1beta/models/{ModelId}:generateContent?key={_apiKey}",
                    //requestBody
                    //);

                    //if (!response.IsSuccessStatusCode)
                    //{
                    //    var error = await response.Content.ReadAsStringAsync();
                    //    await _logger.LogErrorAsync($"Error occured in gemini api call : {error}");
                    //    throw new Exception($"Gemini API Error: {error}");
                    //}
                    //generationConfig = generationConfig;
                    //var res = await response.Content.ReadAsStringAsync();
                    var res = await GetGeminiApiResponseAsync(reqBody);
                   return (res, res?.UsageMetadata);

                },
                $"gemini-{ModelId}"
        );
        // Extract only the text content from the Gemini response wrapper
        return CleanGeminiJson(result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text);


    }
    public string CleanGeminiJson(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse)) return rawResponse;

        // Remove opening ```json or ```
        if (rawResponse.StartsWith("```"))
        {
            // Find the end of the first line (the identifier like ```json)
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
    public async Task<string> GetGeminiApiResponseAsync2(object requestBody)
    {
        const int maxRetries = 3;
        const int baseDelayMs = 1000;

        try
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                using var client = new HttpClient();
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, _apiURL);
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken.DecodeBase64());
                httpRequest.Content = JsonContent.Create(requestBody);

                HttpResponseMessage response = null;
                try
                {
                    response = await client.SendAsync(httpRequest);
                }
                catch (Exception sendEx)
                {
                    await _logger.LogErrorAsync($"Error occured in gemini api call (network): {sendEx.Message}:{sendEx.StackTrace}");
                    if (attempt == maxRetries) return null;
                    var wait = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt - 1));
                    await Task.Delay(wait + TimeSpan.FromMilliseconds(new Random().Next(0, 250)));
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return content;
                }

                // Handle 429 - Too Many Requests
                if ((int)response.StatusCode == 429 || response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    await _logger.LogErrorAsync($"Gemini API returned 429 Too Many Requests. Attempt {attempt}/{maxRetries}.");

                    if (attempt == maxRetries)
                    {
                        await _logger.LogErrorAsync("Max retry attempts reached for Gemini API (HTTP). Returning null.");
                        return null;
                    }

                    // Honor Retry-After header if present, otherwise exponential backoff
                    TimeSpan retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt - 1));
                    await Task.Delay(retryAfter + TimeSpan.FromMilliseconds(new Random().Next(0, 250)));
                    continue;
                }

                // Non-retryable error - log and return body
                await _logger.LogErrorAsync($"Error occured in gemini api call : {content}");
                return content;
            }

            return null;
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync($"Error occured in gemini api call : {ex.Message}:{ex.StackTrace}");
            return null;
        }
    }

    public async Task<Google.Cloud.AIPlatform.V1.GenerateContentResponse> GetGeminiApiResponseAsync(GenerateContentRequest requestBody)
    {
        const int maxRetries = 3;
        const int baseDelayMs = 1000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var exist = File.Exists("/home/ec2-user/gen-lang-client-0445687823-e31287759ab4.json");
                if(!exist)
                {
                    await _logger.LogErrorAsync($"File does not exist");
                }
                // 1. Initialize the client using the Builder for specific regions
                var client = await new PredictionServiceClientBuilder
                {
                    Endpoint = "us-central1-aiplatform.googleapis.com"
                }.BuildAsync();

                // 2. Make the call
                var response = await client.GenerateContentAsync(requestBody);
                return response;
            }
            catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.ResourceExhausted || rpcEx.StatusCode == StatusCode.Unavailable || rpcEx.StatusCode == StatusCode.DeadlineExceeded)
            {
                await _logger.LogErrorAsync($"Gemini API rate limited (RPC). Attempt {attempt}/{maxRetries}. Error: {rpcEx.Status.Detail}");
                if (attempt == maxRetries)
                {
                    await _logger.LogErrorAsync("Max retry attempts reached for Gemini API (RPC). Returning null.");
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
                    await _logger.LogErrorAsync($"Gemini API rate limited (message). Attempt {attempt}/{maxRetries}. Error: {msg}");
                    if (attempt == maxRetries)
                    {
                        await _logger.LogErrorAsync("Max retry attempts reached for Gemini API (message). Returning null.");
                        return null;
                    }
                    var backoff = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt - 1));
                    await Task.Delay(backoff + TimeSpan.FromMilliseconds(new Random().Next(0, 250)));
                    continue;
                }

                await _logger.LogErrorAsync($"Error occurred in Gemini API call: {ex.Message}");
                return null;
            }
        }

        return null;
    }

    public async Task<string> GetGeminiApiResponseAsync3(object requestBody)
    {
        const int maxRetries = 3;
        const int baseDelayMs = 1000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                System.Environment.SetEnvironmentVariable("AWS_REGION", "ap-south-1");
                System.Environment.SetEnvironmentVariable("AWS_DEFAULT_REGION", "ap-south-1");
                var credential = await GoogleCredential.GetApplicationDefaultAsync();

                var token = await credential
                    .UnderlyingCredential
                    .GetAccessTokenForRequestAsync();

                using var client = new HttpClient();

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, _apiURL);
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                httpRequest.Content = JsonContent.Create(requestBody);

                var response = await client.SendAsync(httpRequest);

                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return content;
                }

                if ((int)response.StatusCode == 429 || response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    await _logger.LogErrorAsync($"Gemini API returned 429 Too Many Requests. Attempt {attempt}/{maxRetries}.");
                    if (attempt == maxRetries)
                    {
                        await _logger.LogErrorAsync("Max retry attempts reached for Gemini API (HTTP with token). Returning null.");
                        return null;
                    }

                    TimeSpan retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt - 1));
                    await Task.Delay(retryAfter + TimeSpan.FromMilliseconds(new Random().Next(0, 250)));
                    continue;
                }

                await _logger.LogErrorAsync($"Error occured in gemini api call : {content}");
                return content;
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync($"Error occured in gemini api call : {ex.Message}:{ex.StackTrace}");
                if (attempt == maxRetries) return null;
                await Task.Delay(TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt - 1)));
            }
        }

        return null;
    }

    public static Google.Cloud.AIPlatform.V1.GenerateContentRequest ConvertToGenerateContentRequest(
    string prompt,
    string xmlContent,
    System.Collections.Generic.IEnumerable<dynamic> toolsList)
    {
        // Create request
        var request = new Google.Cloud.AIPlatform.V1.GenerateContentRequest
        {
            Model = Endpoint,
            SystemInstruction = new Google.Cloud.AIPlatform.V1.Content
            {
                Parts =
            {
                new Google.Cloud.AIPlatform.V1.Part
                {
                    Text = prompt
                }
            }
            }
        };

        // Add user content
        request.Contents.Add(new Google.Cloud.AIPlatform.V1.Content
        {
            Role = "user",
            Parts =
        {
            new Google.Cloud.AIPlatform.V1.Part
            {
                Text = xmlContent
            }
        }
        });

        // Add tools if available
        if (toolsList != null && toolsList.Count()>0)
        {
            request.Tools.Add(new Google.Cloud.AIPlatform.V1.Tool
            {
                GoogleSearch = new Google.Cloud.AIPlatform.V1.Tool.Types.GoogleSearch()
            });
        }

        return request;
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
