using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Centauri.ContentArchitect.Backend.Configuration;
using Centauri.ContentArchitect.Backend.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Centauri.ContentArchitect.Backend.Services.Clients;

public sealed class DataForSeoClient : IKeywordDataClient, ISerpDataClient, IBacklinkDataClient
{
    private readonly HttpClient _http;
    private readonly DataForSeoOptions _options;
    private readonly IMemoryCache _cache;

    public DataForSeoClient(IHttpClientFactory factory, IOptions<DataForSeoOptions> options, IMemoryCache cache)
    {
        _http = factory.CreateClient();
        _options = options.Value;
        _cache = cache;
    }

    public async Task<KeywordApiResult> GetKeywordDataAsync(string keyword, string country, string language, CancellationToken ct)
    {
        EnsureEnabled();

        // This is the DataForSEO equivalent of Google Ads generateKeywordIdeas: it returns
        // the seed keyword together with Google Ads keyword suggestions and their metrics.
        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/v3/keywords_data/google_ads/keywords_for_keywords/live";
        var payload = new[]
        {
            new
            {
                keywords = new[] { keyword },
                language_code = string.IsNullOrWhiteSpace(language) ? _options.LanguageCode : language,
                location_code = _options.LocationCode,
                include_adult_keywords = false
            }
        };

        var body = await GetCachedResponseBodyAsync(
            $"dataforseo:keyword-ideas:{Normalize(keyword)}:{Normalize(country)}:{Normalize(language, _options.LanguageCode)}:{_options.LocationCode}",
            () => CreateJsonRequest(endpoint, payload),
            ct);

        //var body = System.IO.File.ReadAllText("DataForSeo_Keyword.json");

        using var doc = JsonDocument.Parse(body);
        var task = doc.RootElement.GetProperty("tasks")[0];
        EnsureTaskSucceeded(task);

        var output = new KeywordApiResult();
        if (!task.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array)
            return output;

        foreach (var item in results.EnumerateArray())
        {
            var text = item.TryGetProperty("keyword", out var keywordElement) ? keywordElement.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var concepts = GetStringList(item, "keyword_annotations", "concepts");
            var volume = GetNumber(item, "search_volume");
            var cpc = GetNumber(item, "cpc");
            output.Ideas.Add(new KeywordIdea
            {
                Keyword = text,
                SearchVolume = volume,
                Cpc = cpc,
                CompetitionLevel = GetString(item, "competition"),
                CompetitionIndex = GetNumber(item, "competition_index"),
                LowTopOfPageBid = GetNumber(item, "low_top_of_page_bid"),
                HighTopOfPageBid = GetNumber(item, "high_top_of_page_bid"),
                MonthlySearches = GetMonthlySearches(item),
                Concepts = concepts,
                SearchPartners = item.TryGetProperty("search_partners", out var searchPartners) && searchPartners.ValueKind == JsonValueKind.True
            });
            output.Concepts.AddRange(concepts);
        }

        output.SearchPartners = results.EnumerateArray().Any(x => x.TryGetProperty("search_partners", out var value) && value.ValueKind == JsonValueKind.True);
        output.LocationCode = task.TryGetProperty("data", out var data) && data.TryGetProperty("location_code", out var locationCode)
            ? locationCode.GetInt32()
            : _options.LocationCode;
        output.LanguageCode = task.TryGetProperty("data", out var dataLanguage) && dataLanguage.TryGetProperty("language_code", out var languageCode)
            ? languageCode.GetString() ?? ""
            : (string.IsNullOrWhiteSpace(language) ? _options.LanguageCode : language);

        var primary = output.Ideas.FirstOrDefault(x => x.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                      ?? output.Ideas.FirstOrDefault();
        if (primary is not null)
        {
            output.SearchVolume = primary.SearchVolume;
            output.Cpc = primary.Cpc;
            output.Competition = primary.CompetitionIndex;
            output.CompetitionLevel = primary.CompetitionLevel;
            output.LowTopOfPageBid = primary.LowTopOfPageBid;
            output.HighTopOfPageBid = primary.HighTopOfPageBid;
            output.MonthlySearches = primary.MonthlySearches;
            output.Concepts = primary.Concepts;
            output.SearchPartners = primary.SearchPartners;
        }

        return output;
    }

    public async Task<KeywordIntentData> GetKeywordIntentAsync(string keyword, string country, string language, CancellationToken ct)
    {
        EnsureEnabled();

        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/v3/dataforseo_labs/google/keyword_overview/live";
        var payload = new[]
        {
            new
            {
                keywords = new[] { keyword },
                language_code = string.IsNullOrWhiteSpace(language) ? _options.LanguageCode : language,
                location_code = _options.LocationCode,
                include_serp_info = false,
                include_clickstream_data = false
            }
        };

        var body = await GetCachedResponseBodyAsync(
            $"dataforseo:keyword-intent:{Normalize(keyword)}:{Normalize(country)}:{Normalize(language, _options.LanguageCode)}:{_options.LocationCode}",
            () => CreateJsonRequest(endpoint, payload),
            ct);

        using var doc = JsonDocument.Parse(body);
        var task = doc.RootElement.GetProperty("tasks")[0];
        EnsureTaskSucceeded(task);

        if (!task.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
            return new KeywordIntentData();

        var overview = results[0];
        if (!overview.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array ||
            items.GetArrayLength() == 0)
            return new KeywordIntentData();

        var item = items.EnumerateArray()
            .FirstOrDefault(x => string.Equals(GetString(x, "keyword"), keyword, StringComparison.OrdinalIgnoreCase));
        if (item.ValueKind == JsonValueKind.Undefined)
            item = items[0];

        if (!item.TryGetProperty("search_intent_info", out var intent) || intent.ValueKind != JsonValueKind.Object)
            return new KeywordIntentData();

        return new KeywordIntentData
        {
            MainIntent = GetString(intent, "main_intent") is { Length: > 0 } mainIntent ? mainIntent : "unknown",
            SupplementaryIntents = intent.TryGetProperty("foreign_intent", out var foreignIntent) && foreignIntent.ValueKind == JsonValueKind.Array
                ? foreignIntent.EnumerateArray()
                    .Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => value.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>(),
            LastUpdatedTime = GetString(intent, "last_updated_time")
        };
    }

    public async Task<SerpApiResult> GetSerpAsync(string keyword, string country, string language, CancellationToken ct)
    {
        EnsureEnabled();
        var url = $"{_options.BaseUrl.TrimEnd('/')}/v3/serp/google/organic/live/advanced";
        var payload = new[]
        {
            new
            {
                keyword,
                language_code = string.IsNullOrWhiteSpace(language) ? _options.LanguageCode : language,
                location_code = _options.LocationCode,
                device = _options.SerpDevice,
                os = _options.SerpOs,
                depth = 10
            }
        };

        var body = await GetCachedResponseBodyAsync(
            $"dataforseo:serp:{Normalize(keyword)}:{Normalize(country)}:{Normalize(language, _options.LanguageCode)}:{_options.LocationCode}:{Normalize(_options.SerpDevice)}:{Normalize(_options.SerpOs)}:10",
            () => CreateJsonRequest(url, payload),
            ct);

        // var body = JsonConvert.DeserializeObject<string>(System.IO.File.ReadAllText("DataForSeo_Serp.json"));

        using var doc = JsonDocument.Parse(body);
        var task = doc.RootElement.GetProperty("tasks")[0];
        EnsureTaskSucceeded(task);
        var result = task.GetProperty("result")[0];

        var output = new SerpApiResult
        {
            HasAiOverview = result.TryGetProperty("item_types", out var types) &&
                            types.ToString().Contains("ai_overview", StringComparison.OrdinalIgnoreCase)
        };

        if (result.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var t) ? t.GetString() : "";
                if (type == "organic")
                {
                    output.OrganicResults.Add(new SerpResult
                    {
                        Position = item.TryGetProperty("rank_absolute", out var p) ? p.GetInt32() : output.OrganicResults.Count + 1,
                        Url = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                        Title = item.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                        Domain = item.TryGetProperty("domain", out var d) ? d.GetString() ?? "" : ""
                    });
                }
                else if (type == "people_also_ask")
                {
                    if (item.TryGetProperty("items", out var qs))
                        foreach (var q in qs.EnumerateArray())
                            if (q.TryGetProperty("title", out var text))
                                output.PeopleAlsoAsk.Add(new QuestionItem { Question = text.GetString() ?? "", Source = "PAA" });
                }
                else if (type == "related_searches")
                {
                    if (item.TryGetProperty("items", out var rs))
                        output.RelatedSearches.AddRange(rs.EnumerateArray().Select(x => x.GetString() ?? ""));
                }
            }
        }

        output.HasAds = body.Contains("\"type\":\"paid\"", StringComparison.OrdinalIgnoreCase);
        output.HasVideo = body.Contains("\"type\":\"video\"", StringComparison.OrdinalIgnoreCase);
        output.HasFeaturedSnippet = body.Contains("featured_snippet", StringComparison.OrdinalIgnoreCase);
        return output;
    }

    public async Task<(int ReferringDomains, double DomainStrength)> GetBacklinkDataAsync(string url, CancellationToken ct)
    {
        EnsureEnabled();
        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/v3/backlinks/summary/live";
        var payload = new[] { new { target = url, include_subdomains = true } };

        var body = await GetCachedResponseBodyAsync(
            $"dataforseo:backlinks-summary:{Normalize(url)}:subdomains",
            () => CreateJsonRequest(endpoint, payload),
            ct);

        using var doc = JsonDocument.Parse(body);
        var task = doc.RootElement.GetProperty("tasks")[0];
        EnsureTaskSucceeded(task);
        var result = task.GetProperty("result")[0];

        var rd = result.TryGetProperty("referring_domains", out var r) ? r.GetInt32() : 0;
        var rank = result.TryGetProperty("rank", out var rankEl) ? rankEl.GetDouble() : 0;
        return (rd, Math.Clamp(rank, 0, 100));
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Login}:{_options.Password}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        return req;
    }

    private HttpRequestMessage CreateJsonRequest<T>(string url, T payload)
    {
        var request = CreateRequest(HttpMethod.Post, url);
        request.Content = JsonContent.Create(payload);
        return request;
    }

    private async Task<string> GetCachedResponseBodyAsync(string cacheKey, Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        if (_options.CacheDurationMinutes <= 0)
            return await SendForResponseBodyAsync(requestFactory, ct);

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.CacheDurationMinutes);
            return await SendForResponseBodyAsync(requestFactory, ct);
        }) ?? throw new InvalidOperationException("DataForSEO cache returned no response body.");
    }

    private async Task<string> SendForResponseBodyAsync(Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        using var request = requestFactory();
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        EnsureTaskSucceeded(doc.RootElement.GetProperty("tasks")[0]);
        return body;
    }

    private static string Normalize(string? value, string? fallback = null)
        => string.IsNullOrWhiteSpace(value) ? (fallback ?? "").Trim().ToLowerInvariant() : value.Trim().ToLowerInvariant();

    private static double GetNumber(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0;
    }

    private static string GetString(JsonElement item, string propertyName)
        => item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static List<MonthlySearchVolume> GetMonthlySearches(JsonElement item)
    {
        if (!item.TryGetProperty("monthly_searches", out var monthlySearches) ||
            monthlySearches.ValueKind != JsonValueKind.Array)
            return new List<MonthlySearchVolume>();

        return monthlySearches.EnumerateArray()
            .Where(month => month.ValueKind == JsonValueKind.Object)
            .Select(month => new MonthlySearchVolume
            {
                Year = (int)GetNumber(month, "year"),
                Month = (int)GetNumber(month, "month"),
                SearchVolume = GetNumber(month, "search_volume")
            })
            .OrderByDescending(month => month.Year)
            .ThenByDescending(month => month.Month)
            .ToList();
    }

    private static void EnsureTaskSucceeded(JsonElement task)
    {
        if (!task.TryGetProperty("status_code", out var statusCode) || statusCode.GetInt32() == 20000)
            return;

        var message = task.TryGetProperty("status_message", out var statusMessage)
            ? statusMessage.GetString()
            : "Unknown DataForSEO task error.";
        throw new HttpRequestException($"DataForSEO request failed: {message}");
    }

    private void EnsureEnabled()
    {
        if (!_options.Enabled) throw new InvalidOperationException("DataForSEO is disabled in appsettings.");
        if (string.IsNullOrWhiteSpace(_options.Login) || string.IsNullOrWhiteSpace(_options.Password))
            throw new InvalidOperationException("DataForSEO credentials are missing.");
    }

    private static List<string> GetStringList(JsonElement item, string parentPropertyName, string childPropertyName)
    {
        if (!item.TryGetProperty(parentPropertyName, out var parent) || parent.ValueKind != JsonValueKind.Object)
            return new List<string>();

        if (!parent.TryGetProperty(childPropertyName, out var child) || child.ValueKind != JsonValueKind.Array)
            return new List<string>();

        return child.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString() ?? "")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
