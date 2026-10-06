using Centauri.ContentArchitect.Backend.Configuration;
using Centauri.ContentArchitect.Backend.Models;
using Centauri.ContentArchitect.Backend.Services.Calculators;
using Microsoft.Extensions.Options;

namespace Centauri.ContentArchitect.Backend.Services;

public sealed class ContentArchitectService : IContentArchitectService
{
    private readonly IKeywordDataClient _keywords;
    private readonly ISerpDataClient _serp;
    private readonly IBacklinkDataClient _backlinks;
    private readonly ISearchConsoleClient _gsc;
    private readonly IPublicIndexabilityClient _publicIndexability;
    private readonly IGeminiClient _gemini;
    private readonly IWebPageParser _parser;
    private readonly ISitemapService _sitemap;
    private readonly IKeywordCalculator _keywordCalculator;
    private readonly KeywordDifficultyCalculator _kd;
    private readonly IndexabilityCalculator _indexability;
    private readonly TrafficPotentialCalculator _traffic;
    private readonly QuestionCoverageCalculator _questions;
    private readonly ContentGapCalculator _gaps;
    private readonly EeatInformationGainCalculator _eeat;
    private readonly AnalysisOptions _options;

    public ContentArchitectService(
        IKeywordDataClient keywords, ISerpDataClient serp, IBacklinkDataClient backlinks,
        ISearchConsoleClient gsc, IPublicIndexabilityClient publicIndexability, IGeminiClient gemini, IWebPageParser parser, ISitemapService sitemap,
        IKeywordCalculator keywordCalculator, KeywordDifficultyCalculator kd, IndexabilityCalculator indexability,
        TrafficPotentialCalculator traffic, QuestionCoverageCalculator questions, ContentGapCalculator gaps,
        EeatInformationGainCalculator eeat, IOptions<AnalysisOptions> options)
    {
        _keywords = keywords; _serp = serp; _backlinks = backlinks; _gsc = gsc; _publicIndexability = publicIndexability; _gemini = gemini;
        _parser = parser; _sitemap = sitemap; _keywordCalculator = keywordCalculator; _kd = kd;
        _indexability = indexability; _traffic = traffic; _questions = questions; _gaps = gaps; _eeat = eeat;
        _options = options.Value;
    }

    public async Task<AnalysisResponse> AnalyzeAsync(AnalysisRequest request, CancellationToken ct)
    {
        var warnings = new List<string>();

        var keywordTask = _keywords.GetKeywordDataAsync(request.PrimaryKeyword, request.TargetRegion, request.Language, ct);
        var intentTask = _keywords.GetKeywordIntentAsync(request.PrimaryKeyword, request.TargetRegion, request.Language, ct);
        var serpTask = _serp.GetSerpAsync(request.PrimaryKeyword, request.TargetRegion, request.Language, ct);
        await Task.WhenAll(keywordTask, intentTask, serpTask);

        var keywordApi = await keywordTask;
        var keywordIntent = await intentTask;
        var serpApi = await serpTask;

        var keywordData = new KeywordData
        {
            PrimarySearchVolume = keywordApi.SearchVolume,
            PrimaryCpc = keywordApi.Cpc,
            PrimaryCompetitionLevel = keywordApi.CompetitionLevel,
            PrimaryCompetitionIndex = keywordApi.Competition,
            PrimaryLowTopOfPageBid = keywordApi.LowTopOfPageBid,
            PrimaryHighTopOfPageBid = keywordApi.HighTopOfPageBid,
            PrimaryMonthlySearches = keywordApi.MonthlySearches,
            PrimaryIntent = keywordIntent.MainIntent,
            PrimarySupplementaryIntents = keywordIntent.SupplementaryIntents,
            PrimaryIntentLastUpdatedTime = keywordIntent.LastUpdatedTime,
            SecondaryClusters = BuildClusters(keywordApi, request.PrimaryKeyword)
        };

        var top10 = serpApi.OrganicResults.Take(5).ToList();

        await EnrichTopResultsAsync(request.PrimaryKeyword, top10, warnings, ct);

        var addressable = _keywordCalculator.Calculate(keywordData);
        var kd = _kd.Calculate(top10);

        var siteIndex = await BuildIndexabilityAsync(request.TargetUrl, ct, warnings);
        var indexability = _indexability.Calculate(siteIndex);

        var clickability = CalculateSerpClickability(serpApi);
        var traffic = _traffic.Calculate(
            new[] { new KeywordCluster { CanonicalKeyword = request.PrimaryKeyword, Volume = keywordData.PrimarySearchVolume } }
                .Concat(keywordData.SecondaryClusters).ToList(),
            indexability.ReadinessScore,
            clickability);

        var questionUniverse = BuildQuestionUniverse(serpApi, top10);
        var qAnswered = _questions.Calculate(questionUniverse, top10);

        var alreadyAnsweredQuestions = qAnswered.Questions.Select(x => x.Question).ToList();

        var extraQAi = await _gemini.GenerateAdditionalQuestionsAsync(
            request.PrimaryKeyword,
            questionUniverse,
            alreadyAnsweredQuestions,
            string.Join("\n\n", top10.Select(x => x.Title + "\n" + x.ExtractedText)),
            ct);

        var gaps = _gaps.Calculate(extraQAi.Questions, qAnswered.Questions);

        var contentAggregate = await BuildEeatAggregateAsync(request, top10, qAnswered, gaps, ct);
        var eeat = _eeat.Calculate(contentAggregate);

        return new AnalysisResponse
        {
            PrimaryKeyword = request.PrimaryKeyword,
            TargetRegion = request.TargetRegion,
            Language = request.Language,
            Targeturl = request.TargetUrl,
            Confidence = BuildConfidence(keywordApi, serpApi, siteIndex, warnings),
            Foundational = new FoundationalData
            {
                Keyword = keywordData,
                Top10 = top10,
                PaaQuestions = serpApi.PeopleAlsoAsk,
                RelatedSearches = serpApi.RelatedSearches,
                SiteIndex = siteIndex,
                ContentAnalysis = contentAggregate
            },
            Intermediate = new IntermediateMetrics
            {
                AddressableSearchDemand = addressable.AddressableSearchDemand,
                AuthorityPressure = kd.AuthorityPressure,
                LinkPressure = kd.LinkPressure,
                IntentSaturation = kd.IntentSaturation,
                CompetitorContentStrength = kd.CompetitorContentStrength,
                IndexabilityReadiness = indexability.ReadinessScore,
                SerpClickability = clickability,
                QuestionCoverageAverage = MetricMath.Mean(qAnswered.Questions.Select(x => x.Coverage)),
                EvidenceRequirement = eeat.EvidenceRequirement,
                InformationGainOpportunity = eeat.InformationGainOpportunity
            },
            Metrics = new MetricResults
            {
                TotalSearchVolume = addressable,
                KeywordDifficulty = kd,
                Indexability = indexability,
                TrafficPotential = traffic,
                QuestionsAnswered = qAnswered,
                AdditionalQuestions = gaps,
                EeatInformationGain = eeat
            }
        };
    }

    /// <summary>
    /// Enriches the raw SERP rows before they are used by the difficulty calculator.
    /// SERP providers only return rank, title and URL; Gemini supplies the page-content
    /// metrics and the backlink provider supplies authority/link metrics.
    /// </summary>
    private async Task EnrichTopResultsAsync(
        string keyword, IReadOnlyList<SerpResult> top10, List<string> warnings, CancellationToken ct)
    {
        if (top10.Count == 0)
        {
            warnings.Add("Keyword difficulty could not be calculated because the SERP provider returned no organic results.");
            return;
        }

        var enrichmentLimit = Math.Clamp(_options.MaxEnrichedSerpResults, 1, top10.Count);
        var candidates = top10
            .Where(x => Uri.TryCreate(x.Url, UriKind.Absolute, out _))
            .Take(enrichmentLimit)
            .ToList();

        foreach (var batch in candidates.Chunk(Math.Max(1, _options.EnrichmentParallelism)))
        {
            var results = await Task.WhenAll(batch.Select(result => EnrichTopResultAsync(keyword, result, ct)));
            warnings.AddRange(results.Where(message => message is not null).Select(message => message!));
        }

        if (candidates.Count < top10.Count)
            warnings.Add($"Enriched the top {candidates.Count} SERP results to keep analysis responsive; remaining results use the SERP provider metrics.");

        var analyzedPages = top10.Count(x => x.ContentCoverage > 0 || x.IntentMatch > 0);
        var backlinkRows = top10.Count(x => x.DomainStrength > 0 || x.ReferringDomains > 0);

        if (analyzedPages == 0)
            warnings.Add("Keyword difficulty content metrics are unavailable because no ranking pages could be analyzed by Gemini.");

        if (top10.Count > 0 && backlinkRows == 0)
            warnings.Add("Keyword difficulty authority and link metrics are unavailable because the configured backlink provider returned no data.");
    }

    private async Task<string?> EnrichTopResultAsync(string keyword, SerpResult result, CancellationToken ct)
    {
        using var enrichmentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        enrichmentCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.PerResultEnrichmentTimeoutSeconds)));

        try
        {
            // Backlinks do not depend on page parsing, so start that request immediately.
            var backlinksTask = _backlinks.GetBacklinkDataAsync(result.Url, enrichmentCts.Token);
            var page = await _parser.ParseAsync(result.Url, enrichmentCts.Token);
            var analysis = await _gemini.AnalyzePageAsync(keyword, page.Text, string.Join("\n", page.Headings), enrichmentCts.Token);
            var backlinks = await backlinksTask;

            result.ReferringDomains = Math.Max(0, backlinks.ReferringDomains);
            result.DomainStrength = MetricMath.Clamp(backlinks.DomainStrength);
            result.ContentCoverage = MetricMath.Clamp(analysis.ContentCoverage);
            result.IntentMatch = Math.Clamp(analysis.IntentMatch, 0, 1);
            result.Questions = analysis.Questions ?? new List<string>();
            result.Entities = analysis.Entities ?? new List<string>();
            result.FirstHandEvidenceRate = Math.Clamp(analysis.FirstHandEvidenceRate, 0, 1);
            result.SourceDensity = Math.Max(0, analysis.SourceDensity);
            result.ExtractedText = page.Text;
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return $"Timed out enriching SERP URL {result.Url}; keeping its SERP-provider metrics.";
        }
        catch (Exception ex)
        {
            return $"Could not fully analyze SERP URL {result.Url}: {ex.Message}";
        }
    }

    private async Task<SiteIndexData> BuildIndexabilityAsync(string websiteUrl, CancellationToken ct, List<string> warnings)
    {
        if (string.Equals(_options.IndexabilityMode, "PublicEstimate", StringComparison.OrdinalIgnoreCase))
            return await BuildPublicIndexabilityAsync(websiteUrl, ct, warnings);

        try
        {
            var urls = await _sitemap.GetUrlsAsync(websiteUrl, _options.Sampling.MaxSitemapUrls, ct);
            if (urls.Count == 0)
            {
                warnings.Add("No sitemap URLs were found; sitemap health and index rate cannot be measured reliably.");
                return new SiteIndexData { SitemapUrlCount = 0, SitemapHealth = 0 };
            }

            // MVP sampling. The document recommends stratified sampling; this implementation
            // keeps a deterministic sample while exposing the max sample in appsettings.
            var sample = urls.OrderBy(x => StableHash(x)).Take(_options.Sampling.MaxGscInspectionsPerRun).ToList();
            var inspections = await _gsc.InspectUrlsAsync(websiteUrl, sample, ct);

            return new SiteIndexData
            {
                SitemapUrlCount = urls.Count,
                InspectedUrls = inspections.Count,
                IndexedPassCount = inspections.Count(x => x.Indexed),
                HistoricalIndexRate = inspections.Count == 0 ? 0 : 100.0 * inspections.Count(x => x.Indexed) / inspections.Count,
                CrawlHealth = inspections.Count == 0 ? 0 : 100.0 * inspections.Count(x => x.CrawlOk) / inspections.Count,
                CanonicalConsistency = inspections.Count == 0 ? 0 : 100.0 * inspections.Count(x => x.CanonicalConsistent) / inspections.Count,
                SitemapHealth = 100,
                InternalDiscovery = 50, // Replace with site crawl/internal-link graph in the next persistence-backed phase.
                DomainStrength = 50
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Indexability analysis failed: {ex}");
            return new SiteIndexData();
        }
    }

    private async Task<SiteIndexData> BuildPublicIndexabilityAsync(string websiteUrl, CancellationToken ct, List<string> warnings)
    {
        try
        {
            List<string> sitemapUrls;
            try
            {
                sitemapUrls = await _sitemap.GetUrlsAsync(websiteUrl, _options.Sampling.MaxSitemapUrls, ct);
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not load a sitemap for public indexability estimation: {ex.Message}");
                sitemapUrls = new List<string>();
            }

            var sample = sitemapUrls.Append(websiteUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(StableHash)
                .Take(_options.Sampling.MaxGscInspectionsPerRun)
                .ToList();
            var inspections = await _publicIndexability.InspectUrlsAsync(websiteUrl, sample, ct);
            var inspected = inspections.Count;
            var candidates = inspections.Count(x => x.CrawlOk && !x.IsBlockedByRobots && !x.HasNoindex);

            return new SiteIndexData
            {
                SitemapUrlCount = sitemapUrls.Count,
                InspectedUrls = inspected,
                IndexedPassCount = candidates,
                HistoricalIndexRate = inspected == 0 ? 0 : 100.0 * candidates / inspected,
                CrawlHealth = inspected == 0 ? 0 : 100.0 * inspections.Count(x => x.CrawlOk) / inspected,
                CanonicalConsistency = inspected == 0 ? 0 : 100.0 * inspections.Count(x => x.CanonicalConsistent) / inspected,
                SitemapHealth = sitemapUrls.Count > 0 ? 100 : 0,
                InternalDiscovery = sitemapUrls.Count > 0 ? 50 : 0,
                DomainStrength = 0,
                IsEstimated = true,
                Source = "PublicEstimate"
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Public indexability estimation failed: {ex.Message}");
            return new SiteIndexData { IsEstimated = true, Source = "PublicEstimate" };
        }
    }

    private Task<ContentAnalysisAggregate> BuildEeatAggregateAsync(
        AnalysisRequest request, IReadOnlyList<SerpResult> top10,
        QuestionsAnsweredResult qAnswered, ContentGapsResult gaps, CancellationToken ct)
    {
        var top = top10.Take(10).ToList();
        var n = Math.Max(1, top.Count);

        var redundancy = 0.0;
        var pairs = 0;
        for (var i = 0; i < top.Count; i++)
        for (var j = i + 1; j < top.Count; j++)
        {
            if (string.IsNullOrWhiteSpace(top[i].ExtractedText) || string.IsNullOrWhiteSpace(top[j].ExtractedText)) continue;
            // Pairwise Gemini similarity caused up to 45 additional Vertex calls per
            // analysis. A local token-overlap estimate is sufficient for this
            // aggregate redundancy signal and avoids exhausting model quota.
            redundancy += CalculateTextSimilarity(top[i].ExtractedText, top[j].ExtractedText);
            pairs++;
        }
        redundancy = pairs == 0 ? 0 : redundancy / pairs;

        var avgFer = top.Average(x => x.FirstHandEvidenceRate);
        var avgOriginal = top.Average(x => 0.0); // AI original-data field is page-level and can be added to SerpResult if needed.
        var sourceReq = Math.Clamp(top.Average(x => x.SourceDensity) / 10.0, 0, 1);
        var authority = MetricMath.Clamp(top.Average(x => x.DomainStrength)) / 100.0;

        return Task.FromResult(new ContentAnalysisAggregate
        {
            FirstHandEvidenceRate = avgFer,
            OriginalDataPrevalence = avgOriginal,
            SourceRequirement = sourceReq,
            AuthorityPressure = authority,
            YmyLTopicSensitivity = 0.50,
            FreshnessRequirement = 0.50,
            CompetitorRedundancy = redundancy,
            MissingQuestionCoverage = gaps.Gaps.Count == 0 ? 0 : MetricMath.Mean(gaps.Gaps.Select(x => x.CompetitorGap)),
            MissingEntityCoverage = 0.50,
            MissingEvidenceCoverage = 1.0 - avgFer
        });
    }

    private static double CalculateTextSimilarity(string first, string second)
    {
        var firstTokens = Tokenize(first);
        var secondTokens = Tokenize(second);
        if (firstTokens.Count == 0 || secondTokens.Count == 0) return 0;

        var intersection = firstTokens.Intersect(secondTokens, StringComparer.OrdinalIgnoreCase).Count();
        var union = firstTokens.Union(secondTokens, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static HashSet<string> Tokenize(string text) => text
        .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', ':', ';', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}', '/', '\\', '-' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(token => token.Length > 2)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static List<KeywordCluster> BuildClusters(KeywordApiResult api, string primary)
    {
        return api.Ideas
            .Where(x => !x.Keyword.Equals(primary, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.Keyword.Trim().ToLowerInvariant())
            .Select(g => new KeywordCluster
            {
                CanonicalKeyword = g.First().Keyword,
                Volume = g.Max(x => x.SearchVolume),
                Variants = g.Select(x => x.Keyword).Distinct().ToList()
            })
            .OrderByDescending(x => x.Volume)
            .Take(10)
            .ToList();
    }

    private static List<string> BuildQuestionUniverse(SerpApiResult serp, IReadOnlyList<SerpResult> top10)
    {
        return serp.PeopleAlsoAsk.Select(x => x.Question)
            .Concat(serp.RelatedSearches.Where(x => x.Contains('?')))
            .Concat(top10.SelectMany(x => x.Questions))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private double CalculateSerpClickability(SerpApiResult serp)
    {
        if (serp.HasAiOverview && serp.HasAds) return _options.SerpClickability.HeavyAdsAndAiOverview;
        if (serp.HasAiOverview) return _options.SerpClickability.AiOverview;
        if (serp.HasFeaturedSnippet) return _options.SerpClickability.FeaturedSnippet;
        if (serp.HasPaaOrVideo()) return _options.SerpClickability.HeavyPaaVideo;
        return _options.SerpClickability.Normal;
    }

    private static AnalysisConfidence BuildConfidence(KeywordApiResult k, SerpApiResult s, SiteIndexData i, List<string> warnings)
    {
        var parts = new List<double>
        {
            k.SearchVolume > 0 ? 1 : 0,
            s.OrganicResults.Count >= 10 ? 1 : s.OrganicResults.Count / 10.0,
            i.InspectedUrls > 0 ? 1 : 0
        };
        var avg = parts.Average();
        return new AnalysisConfidence
        {
            Overall = avg >= .85 ? "High" : avg >= .60 ? "Medium" : "Low",
            ByMetric = new Dictionary<string, string>
            {
                ["Metric1"] = k.SearchVolume > 0 ? "High" : "Low",
                ["Metric2"] = s.OrganicResults.Count >= 10 ? "High" : "Low",
                ["Metric3"] = i.InspectedUrls > 0 ? "High" : "Low"
            },
            Warnings = warnings
        };
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in value) hash = hash * 31 + c;
            return hash & int.MaxValue;
        }
    }
}

internal static class SerpExtensions
{
    public static bool HasPaaOrVideo(this SerpApiResult x) => x.PeopleAlsoAsk.Count > 0 || x.HasVideo;
}
