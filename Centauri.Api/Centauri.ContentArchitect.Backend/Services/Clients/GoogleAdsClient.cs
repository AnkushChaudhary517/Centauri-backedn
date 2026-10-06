using System.Text.Json;
using System.Globalization;
using Centauri.ContentArchitect.Backend.Configuration;
using Centauri.ContentArchitect.Backend.Models;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centauri.ContentArchitect.Backend.Services.Clients;

/// <summary>
/// Google Ads API client using Application Default Credentials (ADC) from shared GCP Project.
/// Reuses the same GCP project as GeminiClient - no new project setup needed!
/// </summary>
public sealed class GoogleAdsClient : IKeywordDataClient, ISerpDataClient, IBacklinkDataClient
{
    private readonly HttpClient _http;
    private readonly GoogleAdsOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GoogleAdsClient> _logger;
    private readonly string _gcpProject;
    private readonly string _gcpLocation;
    private readonly string _modelDefault;
    private readonly string _customerId;
    private readonly Lazy<Task<GoogleCredential>> _credential;

    public GoogleAdsClient(
        IHttpClientFactory factory,
        IOptions<GoogleAdsOptions> options,
        IConfiguration config,
        IMemoryCache cache,
        ILogger<GoogleAdsClient> logger)
    {
        _http = factory.CreateClient();
        _options = options.Value;
        _cache = cache;
        _logger = logger;

        // Reuse the same Gemini project/location setup that is working in the app.
        _gcpProject = config["Gemini:ProjectId"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
            ?? Environment.GetEnvironmentVariable("GCLOUD_PROJECT")
            ?? "gen-lang-client-0445687823";

        _gcpLocation = config["Gemini:Location"] ?? "asia-south1";
        _modelDefault = config["Gemini:Model:Default"] ?? "gemini-2.5-flash";

        // Google Ads specific configuration
        _customerId = config["ExternalApis:GoogleAds:CustomerId"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_ADS_CUSTOMER_ID")
            ?? throw new InvalidOperationException("Google Ads Customer ID is not configured");

        // Initialize credential using Application Default Credentials (like GeminiClient)
        _credential = new Lazy<Task<GoogleCredential>>(CreateCredentialAsync);

        if (_gcpProject == "gen-lang-client-0445687823")
            _logger.LogWarning("No Google project configured for GoogleAds; using fallback project which may not be accessible.");
    }

    /// <summary>
    /// Retrieves keyword insights including search volume, CPC, competition, and related keywords.
    /// </summary>
    public async Task<KeywordApiResult> GetKeywordDataAsync(string keyword, string country, string language, CancellationToken ct)
    {
        EnsureEnabled();

        var cacheKey = $"googleads:keyword-ideas:{Normalize(keyword)}:{Normalize(country)}:{Normalize(language, _options.LanguageCode)}";

        if (_options.CacheDurationMinutes > 0 && _cache.TryGetValue(cacheKey, out KeywordApiResult? cachedResult) && cachedResult is not null)
        {
            _logger.LogDebug("Cache hit for keyword data: {Keyword}", keyword);
            return cachedResult;
        }

        _logger.LogInformation("Fetching keyword data from Google Ads API for keyword: {Keyword}", keyword);

        try
        {
            var result = await GenerateKeywordIdeasAsync(keyword, language, country, ct);

            if (_options.CacheDurationMinutes > 0)
            {
                _cache.Set(cacheKey, result, TimeSpan.FromMinutes(_options.CacheDurationMinutes));
            }

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Google Ads API request failed for keyword: {Keyword}", keyword);
            throw new HttpRequestException($"Google Ads API request failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Retrieves search intent data for a keyword.
    /// </summary>
    public async Task<KeywordIntentData> GetKeywordIntentAsync(string keyword, string country, string language, CancellationToken ct)
    {
        EnsureEnabled();

        var cacheKey = $"googleads:keyword-intent:{Normalize(keyword)}:{Normalize(country)}:{Normalize(language, _options.LanguageCode)}";

        if (_options.CacheDurationMinutes > 0 && _cache.TryGetValue(cacheKey, out KeywordIntentData? cachedIntent) && cachedIntent is not null)
        {
            _logger.LogDebug("Cache hit for keyword intent: {Keyword}", keyword);
            return cachedIntent;
        }

        _logger.LogInformation("Fetching keyword intent from Gemini for keyword: {Keyword}", keyword);

        try
        {
            var prompt = $@"You are a keyword intent classifier for SEO research.

Task: classify the keyword from the supplied locale context.
Inputs:
- keyword: '{keyword}'
- country: '{country}'
- language: '{language}'

Return valid JSON only and nothing else. Use this exact schema:
{{
  ""main_intent"": ""informational|commercial|transactional|navigational|unknown"",
  ""supplementary_intents"": [""string""],
  ""last_updated_time"": ""ISO-8601 timestamp""
}}

Rules:
- Choose the primary intent from the exact allowed values.
- supplementary_intents may contain up to 5 related intents.
- Keep the response compact and machine-readable.
- Do not include markdown or comments.";

            var data = await CallVertexGeminiJsonAsync(prompt, ct);
            var payload = data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0 ? data[0] : data;

            var intent = new KeywordIntentData
            {
                MainIntent = TryGetString(payload, "main_intent") ?? TryGetString(payload, "mainIntent") ?? "unknown",
                SupplementaryIntents = ExtractStringList(payload, "supplementary_intents", "supplementaryIntents", "foreign_intent", "foreignIntent"),
                LastUpdatedTime = TryGetString(payload, "last_updated_time") ?? TryGetString(payload, "lastUpdatedTime") ?? DateTime.UtcNow.ToString("O")
            };

            if (string.IsNullOrWhiteSpace(intent.MainIntent))
            {
                intent.MainIntent = "unknown";
            }

            if (_options.CacheDurationMinutes > 0)
            {
                _cache.Set(cacheKey, intent, TimeSpan.FromMinutes(_options.CacheDurationMinutes));
            }

            return intent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to classify keyword intent for: {Keyword}", keyword);
            return new KeywordIntentData
            {
                MainIntent = "unknown",
                SupplementaryIntents = new List<string>(),
                LastUpdatedTime = DateTime.UtcNow.ToString("O")
            };
        }
    }

    /// <summary>
    /// Retrieves SERP data using the live Gemini response contract, not heuristics.
    /// </summary>
    public async Task<SerpApiResult> GetSerpAsync(string keyword, string country, string language, CancellationToken ct)
    {
        EnsureEnabled();

        // Version the cache contract so old sparse SERP snapshots are not reused.
        var cacheKey = $"googleads:serp:v2:{Normalize(keyword)}:{Normalize(country)}:{Normalize(language, _options.LanguageCode)}:{Normalize(_options.SerpDevice)}";

        if (_options.CacheDurationMinutes > 0 && _cache.TryGetValue(cacheKey, out SerpApiResult? cachedSerp) && cachedSerp is not null)
        {
            _logger.LogDebug("Cache hit for SERP data: {Keyword}", keyword);
            return cachedSerp;
        }

        _logger.LogInformation("Fetching SERP data from Gemini for keyword: {Keyword}", keyword);

        try
        {
            var prompt = $@"You are a SERP analysis model for SEO audit workflows.

Goal: return a structured SERP snapshot with every metric required to calculate
keyword difficulty for the given keyword and locale.

Inputs:
- keyword: '{keyword}'
- country: '{country}'
- language: '{language}'
- device: '{_options.SerpDevice}'

Return valid JSON only and nothing else. Use this exact schema:
{{
  ""hasAiOverview"": true,
  ""hasAds"": true,
  ""hasVideo"": true,
  ""hasFeaturedSnippet"": true,
  ""organicResults"": [
    {{
      ""position"": 1,
      ""title"": ""string"",
      ""url"": ""string"",
      ""domain"": ""example.com"",
      ""domainStrength"": 0,
      ""referringDomains"": 0,
      ""contentCoverage"": 0,
      ""intentMatch"": 0.0
    }}
  ],
  ""peopleAlsoAsk"": [
    {{ ""question"": ""string"", ""source"": ""PAA"" }}
  ],
  ""relatedSearches"": [""string""]
}}

Rules:
- Return exactly {_options.SerpDepth} organic results whenever possible, ordered by position.
- Every organic-result object MUST contain every field in the schema. Do not use null or omit a field.
- domainStrength is an estimated 0..100 domain-authority strength.
- referringDomains is a non-negative integer estimate for the ranking URL/domain.
- contentCoverage is 0..100: how comprehensively that page addresses the keyword and its core subtopics.
- intentMatch is 0..1: how directly the page satisfies the dominant intent of the exact keyword.
- Use numeric JSON values for all four difficulty fields, never quoted numeric strings.
- Give best available estimates when an exact metric cannot be established; do not leave a numeric field empty.
- peopleAlsoAsk and relatedSearches must be arrays.
- Keep the JSON compact and valid.
- Do not include markdown or explanations.";

            var data = await CallVertexGeminiJsonAsync(prompt, ct);
            var payload = data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0 ? data[0] : data;

            var serpResult = new SerpApiResult
            {
                HasAiOverview = TryGetBool(payload, "hasAiOverview") || TryGetBool(payload, "has_ai_overview"),
                HasAds = TryGetBool(payload, "hasAds") || TryGetBool(payload, "has_ads"),
                HasVideo = TryGetBool(payload, "hasVideo") || TryGetBool(payload, "has_video"),
                HasFeaturedSnippet = TryGetBool(payload, "hasFeaturedSnippet") || TryGetBool(payload, "has_featured_snippet"),
                OrganicResults = ExtractOrganicResults(payload),
                PeopleAlsoAsk = ExtractQuestionItems(payload),
                RelatedSearches = ExtractStringList(payload, "relatedSearches", "related_searches")
            };

            if (_options.CacheDurationMinutes > 0)
            {
                _cache.Set(cacheKey, serpResult, TimeSpan.FromMinutes(_options.CacheDurationMinutes));
            }

            return serpResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch SERP data for keyword: {Keyword}", keyword);
            return new SerpApiResult
            {
                OrganicResults = new List<SerpResult>(),
                PeopleAlsoAsk = new List<QuestionItem>(),
                RelatedSearches = new List<string>(),
                HasAiOverview = false,
                HasAds = false,
                HasVideo = false,
                HasFeaturedSnippet = false
            };
        }
    }

    /// <summary>
    /// Retrieves backlink data (not available from Google Ads API).
    /// </summary>
    public async Task<(int ReferringDomains, double DomainStrength)> GetBacklinkDataAsync(string url, CancellationToken ct)
    {
        EnsureEnabled();

        var cacheKey = $"googleads:backlinks:{Normalize(url)}";

        if (_options.CacheDurationMinutes > 0 && _cache.TryGetValue(cacheKey, out (int, double) cachedBacklinks))
        {
            _logger.LogDebug("Cache hit for backlink data: {Url}", url);
            return cachedBacklinks;
        }

        _logger.LogInformation("Fetching backlink data for URL: {Url}", url);

        try
        {
            var backlinks = await FetchBacklinkDataAsync(url, ct);

            if (_options.CacheDurationMinutes > 0)
            {
                _cache.Set(cacheKey, backlinks, TimeSpan.FromMinutes(_options.CacheDurationMinutes));
            }

            return backlinks;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch backlink data for URL: {Url}", url);
            return (0, 0);
        }
    }

    // ============================================================================
    // Private API Implementation Methods
    // ============================================================================

    /// <summary>
    /// Fetches keyword ideas using Vertex AI (which both GeminiClient and GoogleAdsClient share).
    /// </summary>
    private async Task<KeywordApiResult> GenerateKeywordIdeasAsync(string keyword, string language, string country, CancellationToken ct)
    {
        EnsureEnabled();

        var accessToken = await GetAccessTokenAsync();
        var url = $"https://{_gcpLocation}-aiplatform.googleapis.com/v1/projects/{Uri.EscapeDataString(_gcpProject)}/locations/{Uri.EscapeDataString(_gcpLocation)}/publishers/google/models/{Uri.EscapeDataString(_modelDefault)}:generateContent";

        var prompt = $"Generate keyword ideas for '{keyword}' for country '{country}' and language '{language}'. " +
            "Return exactly one complete JSON object only with fields: keyword, searchVolume, cpc, competitionLevel, monthlySearches, ideas. " +
            "Do not return markdown, explanations, comments, or text before/after the JSON. " +
            "Keep arrays compact and finish the JSON object before the output limit.";

        HttpRequestMessage CreateRequest()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = JsonContent.Create(new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = prompt } }
                    }
                }
                ,
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    temperature = 0.1,
                    maxOutputTokens = 4096
                }
            });
            return request;
        }

        using var response = await VertexAiRequestLimiter.SendAsync(_http, CreateRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Vertex AI request failed for GoogleAds keyword ideas. StatusCode={StatusCode}. Response={Response}", response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        return ParseKeywordIdeasResponse(body, keyword);
    }

    /// <summary>
    /// Fetches backlink data from external services (Moz, Ahrefs, Semrush).
    /// </summary>
    private async Task<(int ReferringDomains, double DomainStrength)> FetchBacklinkDataAsync(string url, CancellationToken ct)
    {
        var backlinkProvider = _options.BacklinkProvider?.ToLowerInvariant() ?? "none";

        return backlinkProvider switch
        {
            "moz" => await FetchBacklinksFromMozAsync(url, ct),
            "ahrefs" => await FetchBacklinksFromAhrefsAsync(url, ct),
            "semrush" => await FetchBacklinksFromSemrushAsync(url, ct),
            _ => (0, 0)
        };
    }

    private async Task<(int ReferringDomains, double DomainStrength)> FetchBacklinksFromMozAsync(string url, CancellationToken ct)
    {
        try
        {
            var mozApiKey = _options.MozApiKey ?? Environment.GetEnvironmentVariable("MOZ_API_KEY");
            if (string.IsNullOrWhiteSpace(mozApiKey))
            {
                _logger.LogWarning("Moz API key not configured");
                return (0, 0);
            }

            var endpoint = $"https://api.moz.com/v2/link-metrics?target={Uri.EscapeDataString(url)}&scope=domain_to_domain&access_token={mozApiKey}";

            using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await _http.SendAsync(req, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return (0, 0);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var domainAuth = root.TryGetProperty("domain_authority", out var da)
                ? Math.Clamp(da.GetDouble(), 0, 100)
                : 0;

            return (0, domainAuth);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Moz backlink data");
            return (0, 0);
        }
    }

    private async Task<(int ReferringDomains, double DomainStrength)> FetchBacklinksFromAhrefsAsync(string url, CancellationToken ct)
    {
        try
        {
            var ahrefsKey = _options.AhrefsApiKey ?? Environment.GetEnvironmentVariable("AHREFS_API_KEY");
            if (string.IsNullOrWhiteSpace(ahrefsKey))
            {
                _logger.LogWarning("Ahrefs API key not configured");
                return (0, 0);
            }

            var endpoint = $"https://api.ahrefs.com/v3/domain-rating?target={Uri.EscapeDataString(url)}&mode=domain&token={ahrefsKey}";

            using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await _http.SendAsync(req, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return (0, 0);

            using var doc = JsonDocument.Parse(body);
            return (0, Math.Clamp(doc.RootElement.GetProperty("domain_rating").GetDouble(), 0, 100));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Ahrefs backlink data");
            return (0, 0);
        }
    }

    private async Task<(int ReferringDomains, double DomainStrength)> FetchBacklinksFromSemrushAsync(string url, CancellationToken ct)
    {
        try
        {
            var semrushKey = _options.SemrushApiKey ?? Environment.GetEnvironmentVariable("SEMRUSH_API_KEY");
            if (string.IsNullOrWhiteSpace(semrushKey))
            {
                _logger.LogWarning("Semrush API key not configured");
                return (0, 0);
            }

            var endpoint = $"https://api.semrush.com/?type=domain_rank&key={semrushKey}&domain={Uri.EscapeDataString(url)}&export_columns=rnk";

            using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await _http.SendAsync(req, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return (0, 0);

            var rank = ParseSemrushDomainRank(body);
            return (0, Math.Clamp(rank, 0, 100));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Semrush backlink data");
            return (0, 0);
        }
    }

    /// <summary>
    /// Gets access token using Application Default Credentials (ADC).
    /// Works exactly like GeminiClient because they share the same GCP project!
    /// </summary>
    private async Task<string> GetAccessTokenAsync()
    {
        try
        {
            var credential = await _credential.Value;
            if (credential.IsCreateScopedRequired)
            {
                credential = credential.CreateScoped("https://www.googleapis.com/auth/cloud-platform");
            }

            return await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to obtain access token using ADC for GoogleAds");
            throw new InvalidOperationException("Could not obtain Google Cloud access token using Application Default Credentials", ex);
        }
    }

    private static async Task<GoogleCredential> CreateCredentialAsync()
    {
        return await GoogleCredential.GetApplicationDefaultAsync();
    }

    // ============================================================================
    // Parsing Helpers
    // ============================================================================

    private static List<string> ExtractStringList(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetArray(element, propertyName, out var values))
            {
                var list = new List<string>();
                foreach (var value in values.EnumerateArray())
                {
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        var text = value.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                            list.Add(text);
                    }
                    else if (value.ValueKind != JsonValueKind.Null)
                    {
                        var text = value.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                            list.Add(text);
                    }
                }

                return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        return new List<string>();
    }

    private static List<SerpResult> ExtractOrganicResults(JsonElement element)
    {
        var results = new List<SerpResult>();
        var organicResults = GetArray(element, "organicResults", "organic_results");

        foreach (var item in organicResults)
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            results.Add(new SerpResult
            {
                Position = item.TryGetProperty("position", out var position) && TryGetInt32(position, out var positionValue)
                    ? positionValue
                    : results.Count + 1,
                Title = TryGetString(item, "title") ?? string.Empty,
                Url = TryGetString(item, "url") ?? string.Empty,
                Domain = TryGetString(item, "domain") ?? string.Empty,
                DomainStrength = Math.Clamp(TryGetDouble(item, "domainStrength"), 0, 100),
                ReferringDomains = (int)Math.Clamp(Math.Round(TryGetDouble(item, "referringDomains")), 0, int.MaxValue),
                ContentCoverage = Math.Clamp(TryGetDouble(item, "contentCoverage"), 0, 100),
                IntentMatch = Math.Clamp(TryGetDouble(item, "intentMatch"), 0, 1)
            });
        }

        return results;
    }

    private static List<QuestionItem> ExtractQuestionItems(JsonElement element)
    {
        var items = new List<QuestionItem>();
        var questionItems = GetArray(element, "peopleAlsoAsk", "people_also_ask");

        foreach (var item in questionItems)
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            items.Add(new QuestionItem
            {
                Question = TryGetString(item, "question") ?? string.Empty,
                Source = TryGetString(item, "source") ?? "PAA"
            });
        }

        return items;
    }

    private static JsonElement[] GetArray(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.ValueKind == JsonValueKind.Object && TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray().ToArray();
            }
        }

        return Array.Empty<JsonElement>();
    }

    private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && TryGetProperty(element, propertyName, out value) && value.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        var normalizedTarget = NormalizeJsonKey(propertyName);
        foreach (var property in element.EnumerateObject())
        {
            if (NormalizeJsonKey(property.Name) == normalizedTarget)
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string NormalizeJsonKey(string value)
        => new string((value ?? string.Empty).Where(ch => char.IsLetterOrDigit(ch)).ToArray()).ToLowerInvariant();

    private KeywordApiResult ParseKeywordIdeasResponse(string jsonBody, string primaryKeyword)
    {
        var output = new KeywordApiResult();

        try
        {
            using var doc = JsonDocument.Parse(jsonBody);
            var root = doc.RootElement;

            var modelText = string.Empty;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("candidates", out var candidates)
                    && candidates.ValueKind == JsonValueKind.Array
                    && candidates.GetArrayLength() > 0
                    && candidates[0].TryGetProperty("content", out var content)
                    && content.TryGetProperty("parts", out var parts)
                    && parts.ValueKind == JsonValueKind.Array
                    && parts.GetArrayLength() > 0
                    && parts[0].TryGetProperty("text", out var textNode))
                {
                    modelText = textNode.GetString() ?? string.Empty;
                }
                else if (root.TryGetProperty("content", out var contentRoot)
                    && contentRoot.TryGetProperty("parts", out var partsRoot)
                    && partsRoot.ValueKind == JsonValueKind.Array
                    && partsRoot.GetArrayLength() > 0
                    && partsRoot[0].TryGetProperty("text", out var textRoot))
                {
                    modelText = textRoot.GetString() ?? string.Empty;
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                modelText = root.GetRawText();
            }

            if (string.IsNullOrWhiteSpace(modelText) || modelText.Trim() == "{}" || modelText.Trim() == "[]")
            {
                _logger.LogWarning("No usable keyword data was returned from Vertex AI. Raw body: {Body}", jsonBody);
                return output;
            }

            if (!TryParseModelJson(modelText, out var parsed))
            {
                _logger.LogWarning("Vertex returned incomplete or non-JSON keyword data; ignoring it rather than parsing a partial payload.");
                return output;
            }

            if (parsed.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in parsed.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    var keyword = item.TryGetProperty("keyword", out var keywordNode)
                        ? keywordNode.GetString() ?? string.Empty
                        : string.Empty;

                    if (string.IsNullOrWhiteSpace(keyword) && item.TryGetProperty("query", out var queryNode))
                    {
                        keyword = queryNode.GetString() ?? string.Empty;
                    }

                    var searchVolume = TryGetDouble(item, "searchVolume");
                    var cpc = TryGetDouble(item, "cpc");
                    var competitionIndex = TryGetDouble(item, "competition") > 0
                        ? TryGetDouble(item, "competition")
                        : TryGetDouble(item, "competitionIndex") > 0
                            ? TryGetDouble(item, "competitionIndex")
                            : 0.5;
                    var competitionLevel = TryGetString(item, "competitionLevel") ?? "Unknown";

                    var keywordIdea = new KeywordIdea
                    {
                        Keyword = keyword,
                        SearchVolume = searchVolume,
                        Cpc = cpc,
                        CompetitionLevel = competitionLevel,
                        CompetitionIndex = competitionIndex,
                        MonthlySearches = ParseMonthlySearches(item)
                    };

                    output.Ideas.Add(keywordIdea);

                    if (item.TryGetProperty("ideas", out var ideasElement) && ideasElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ideaText in ideasElement.EnumerateArray())
                        {
                            var text = ideaText.ValueKind == JsonValueKind.String ? ideaText.GetString() : ideaText.ToString();
                            if (string.IsNullOrWhiteSpace(text)) continue;

                            output.Ideas.Add(new KeywordIdea
                            {
                                Keyword = text,
                                SearchVolume = searchVolume,
                                Cpc = cpc,
                                CompetitionLevel = competitionLevel,
                                CompetitionIndex = competitionIndex,
                                MonthlySearches = new List<MonthlySearchVolume>()
                            });
                        }
                    }
                }
            }
            else if (parsed.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in parsed.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array && prop.Name.Equals("ideas", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var idea in prop.Value.EnumerateArray())
                        {
                            var keyword = idea.ValueKind == JsonValueKind.String ? idea.GetString() : idea.ToString();
                            if (string.IsNullOrWhiteSpace(keyword)) continue;

                            output.Ideas.Add(new KeywordIdea
                            {
                                Keyword = keyword,
                                SearchVolume = TryGetDouble(parsed, "searchVolume"),
                                Cpc = TryGetDouble(parsed, "cpc"),
                                CompetitionLevel = TryGetString(parsed, "competitionLevel") ?? "Unknown",
                                CompetitionIndex = TryGetDouble(parsed, "competition") > 0 ? TryGetDouble(parsed, "competition") : TryGetDouble(parsed, "competitionIndex") > 0 ? TryGetDouble(parsed, "competitionIndex") : 0.5,
                                MonthlySearches = ParseMonthlySearches(parsed)
                            });
                        }
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.Object && prop.Name.Equals("keywordData", StringComparison.OrdinalIgnoreCase))
                    {
                        var inner = prop.Value;
                        var keyword = TryGetString(inner, "keyword") ?? primaryKeyword;
                        output.Ideas.Add(new KeywordIdea
                        {
                            Keyword = keyword,
                            SearchVolume = TryGetDouble(inner, "searchVolume"),
                            Cpc = TryGetDouble(inner, "cpc"),
                            CompetitionLevel = TryGetString(inner, "competitionLevel") ?? "Unknown",
                            CompetitionIndex = TryGetDouble(inner, "competition") > 0 ? TryGetDouble(inner, "competition") : TryGetDouble(inner, "competitionIndex") > 0 ? TryGetDouble(inner, "competitionIndex") : 0.5,
                            MonthlySearches = ParseMonthlySearches(inner)
                        });
                    }
                }

                if (output.Ideas.Count == 0 && parsed.TryGetProperty("keyword", out var keywordProp))
                {
                    var keyword = keywordProp.GetString() ?? primaryKeyword;
                    var idea = new KeywordIdea
                    {
                        Keyword = keyword,
                        SearchVolume = TryGetDouble(parsed, "searchVolume"),
                        Cpc = TryGetDouble(parsed, "cpc"),
                        CompetitionLevel = TryGetString(parsed, "competitionLevel") ?? "Unknown",
                        CompetitionIndex = TryGetDouble(parsed, "competition") > 0 ? TryGetDouble(parsed, "competition") : TryGetDouble(parsed, "competitionIndex") > 0 ? TryGetDouble(parsed, "competitionIndex") : 0.5,
                        MonthlySearches = ParseMonthlySearches(parsed)
                    };

                    output.Ideas.Add(idea);
                }
            }

            if (output.Ideas.Count > 0)
            {
                var top = output.Ideas[0];
                output.SearchVolume = top.SearchVolume;
                output.Cpc = top.Cpc;
                output.Competition = top.CompetitionIndex;
                output.CompetitionLevel = top.CompetitionLevel;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse GoogleAds keyword ideas response. Raw body: {Body}", jsonBody);
        }

        return output;
    }

    private static bool TryGetBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && TryGetProperty(element, propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsedBool)) return parsedBool;
        }

        return false;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && TryGetProperty(element, propertyName, out var value))
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        return null;
    }

    private static double TryGetDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && TryGetProperty(element, propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
                return number;

            if (value.ValueKind == JsonValueKind.String && double.TryParse(
                    value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedNumber))
                return parsedNumber;
        }

        return 0;
    }

    // JsonElement.TryGetInt32 throws for a JSON string. Gemini may return a
    // numeric value as text (for example, "year": "2025"), so inspect the
    // token kind before converting it.
    private static bool TryGetInt32(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetInt32(out value);

        if (element.ValueKind == JsonValueKind.String)
        {
            return int.TryParse(
                element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        value = default;
        return false;
    }

    private static List<MonthlySearchVolume> ParseMonthlySearches(JsonElement element)
    {
        var monthlySearches = new List<MonthlySearchVolume>();

        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("monthlySearches", out var monthlyElement))
            return monthlySearches;

        if (monthlyElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in monthlyElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                monthlySearches.Add(new MonthlySearchVolume
                {
                    SearchVolume = TryGetDouble(item, "searchVolume"),
                    Year = item.TryGetProperty("year", out var year) && TryGetInt32(year, out var y) ? y : DateTime.UtcNow.Year,
                    Month = item.TryGetProperty("month", out var month) && TryGetInt32(month, out var m) ? m : 1
                });
            }
        }
        else if (monthlyElement.ValueKind == JsonValueKind.Number)
        {
            monthlySearches.Add(new MonthlySearchVolume
            {
                SearchVolume = monthlyElement.GetDouble(),
                Year = DateTime.UtcNow.Year,
                Month = DateTime.UtcNow.Month
            });
        }

        return monthlySearches;
    }

    /// <summary>
    /// Infers keyword intent based on keyword characteristics.
    /// </summary>
    private KeywordIntentData InferKeywordIntent(string keyword)
    {
        var keywordLower = keyword.ToLowerInvariant();

        var mainIntent = keywordLower switch
        {
            var k when k.Contains("how to") || k.Contains("what is") || k.Contains("guide") => "informational",
            var k when k.Contains("buy") || k.Contains("price") || k.Contains("cost") => "transactional",
            var k when k.Contains("near me") || k.Contains("local") => "navigational",
            var k when k.Contains("best") || k.Contains("review") => "commercial",
            _ => "unknown"
        };

        return new KeywordIntentData
        {
            MainIntent = mainIntent,
            SupplementaryIntents = new List<string>(),
            LastUpdatedTime = DateTime.UtcNow.ToString("O")
        };
    }

    private bool IsVideoKeyword(string keyword)
        => new[] { "how to", "tutorial", "video", "demo", "review" }
            .Any(k => keyword.Contains(k, StringComparison.OrdinalIgnoreCase));

    private bool ShouldHaveAiOverview(string keyword)
        => !keyword.Contains("buy", StringComparison.OrdinalIgnoreCase) &&
           !keyword.Contains("price", StringComparison.OrdinalIgnoreCase);

    private bool IsQuestionKeyword(string keyword)
        => keyword.Contains('?') || keyword.StartsWith("how", StringComparison.OrdinalIgnoreCase);

    private double ParseSemrushDomainRank(string responseBody)
    {
        try
        {
            var lines = responseBody.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 && double.TryParse(lines[0], out var rank) ? rank : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string Normalize(string? value, string? fallback = null)
        => string.IsNullOrWhiteSpace(value)
            ? (fallback ?? "").Trim().ToLowerInvariant()
            : value.Trim().ToLowerInvariant();

    private void EnsureEnabled()
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("Google Ads client is disabled in appsettings.");
        if (string.IsNullOrWhiteSpace(_customerId))
            throw new InvalidOperationException("Google Ads Customer ID is missing.");
    }

    private async Task<JsonElement> CallVertexGeminiJsonAsync(string prompt, CancellationToken ct)
    {
        var accessToken = await GetAccessTokenAsync();
        var url = $"https://{_gcpLocation}-aiplatform.googleapis.com/v1/projects/{Uri.EscapeDataString(_gcpProject)}/locations/{Uri.EscapeDataString(_gcpLocation)}/publishers/google/models/{Uri.EscapeDataString(_modelDefault)}:generateContent";

        // Apply this at the transport boundary so every Gemini call using this
        // helper follows the same machine-readable response contract.
        var jsonOnlyPrompt = prompt + "\n\nJSON output rules: return exactly one complete JSON object or array. " +
            "Do not use markdown fences, prose, comments, or text before/after the JSON. " +
            "Keep the response within the output limit; if needed, shorten arrays rather than truncating JSON.";

        HttpRequestMessage CreateRequest()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = JsonContent.Create(new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = jsonOnlyPrompt } }
                    }
                }
                ,
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    temperature = 0.1,
                    topP = 0.95,
                    maxOutputTokens = 4096
                }
            });
            return request;
        }

        using var response = await VertexAiRequestLimiter.SendAsync(_http, CreateRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Vertex AI JSON call failed. StatusCode={StatusCode}. Response={Response}", response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        var firstCandidate = candidates[0];
        if (!firstCandidate.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array
            || parts.GetArrayLength() == 0
            || !parts[0].TryGetProperty("text", out var textNode))
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        var text = textNode.GetString() ?? "{}";
        if (!TryParseModelJson(text, out var modelJson))
        {
            _logger.LogWarning("Gemini returned incomplete or non-JSON content; ignoring it rather than parsing a partial payload.");
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        return modelJson;
    }

    private static bool TryParseModelJson(string value, out JsonElement json)
    {
        json = default;
        if (!TryExtractCompleteJson(value, out var completeValue)) return false;

        try
        {
            using var document = JsonDocument.Parse(completeValue);
            json = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts the first syntactically complete JSON object or array from a model
    /// response. Any prose, markdown fence, or data after that value is ignored.
    /// An unfinished value is never passed to JsonDocument.Parse.
    /// </summary>
    private static bool TryExtractCompleteJson(string value, out string json)
    {
        json = string.Empty;
        var start = -1;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] is '{' or '[')
            {
                start = index;
                break;
            }
        }

        if (start < 0) return false;

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = start; index < value.Length; index++)
        {
            var character = value[index];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == '"') inString = false;
                continue;
            }

            if (character == '"') inString = true;
            else if (character is '{' or '[') depth++;
            else if (character is '}' or ']')
            {
                depth--;
                if (depth == 0)
                {
                    json = value[start..(index + 1)];
                    return true;
                }
            }
        }

        return false;
    }
}
