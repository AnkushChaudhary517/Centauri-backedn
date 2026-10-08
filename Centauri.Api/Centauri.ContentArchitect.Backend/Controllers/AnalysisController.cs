using Centauri.ContentArchitect.Backend.Models;
using Centauri.ContentArchitect.Backend.Services;
using Centauri.ContentArchitect.Backend.Configuration;
using Centauri.ContentArchitect.Backend.Extensions;
using CentauriSeo.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Centauri.ContentArchitect.Backend.Controllers;

[ApiController]
[Route("api/content-architect")]
public sealed class AnalysisController : ControllerBase
{
    private readonly IContentArchitectService _service;
    private readonly IGeminiClient _gemini;
    private readonly IMemoryCache _cache;
    private readonly AnalysisOptions _analysisOptions;
    private readonly IKeywordDataClient _keywords;
    private readonly IAnalysisProgressReporter _progressReporter;

    public AnalysisController(IContentArchitectService service, IGeminiClient gemini, IMemoryCache cache, IOptions<AnalysisOptions> analysisOptions,
        IKeywordDataClient keywords, IAnalysisProgressReporter progressReporter)
    {
        _service = service;
        _gemini = gemini;
        _cache = cache;
        _analysisOptions = analysisOptions.Value;
        _keywords = keywords;
        _progressReporter = progressReporter;
    }

    [HttpPost("analyze-kwyword")]
    public async Task<ActionResult<KeywordApiResult>> AnalyzeKeyword([FromBody] AnalysisRequest request, CancellationToken cancellationToken)
    {
        var res =  await _keywords.GetKeywordDataAsync(request.PrimaryKeyword, request.TargetRegion, request.Language, cancellationToken);
        return Ok(res);
    }

    [HttpPost("analyze")]
    public async Task<ActionResult<AnalysisResponse>> Analyze(
        [FromBody] AnalysisRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PrimaryKeyword))
            return BadRequest("PrimaryKeyword is required.");
        if (string.IsNullOrWhiteSpace(request.TargetUrl))
            return BadRequest("WebsiteUrl is required.");

        try
        {
            var cacheKey = $"analyze__{request.PrimaryKeyword}_{request.TargetRegion}_{request.TargetUrl}";
            var cacheKeyStarted = $"{cacheKey}__started";
            var requestIdCacheKey = $"{cacheKey}__requestId";

            var cachedData = _cache.Get(cacheKey);
            var isAnalysisStarted = _cache.Get(cacheKeyStarted);

            // If analysis is already in progress, return cached data with current progress
            if (isAnalysisStarted != null)
            {
                var progressRequestId = _cache.Get<string>(requestIdCacheKey) ?? Guid.NewGuid().ToString();
                _progressReporter.Report(progressRequestId, 2, "in_progress", "Analysis is already running. Retrieving current progress...");

                if (cachedData != null)
                {
                    var options = new System.Text.Json.JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true,
                        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                    };
                    var cachedResponse = System.Text.Json.JsonSerializer.Deserialize<AnalysisResponse>(cachedData?.ToString(), options);
                    if (cachedResponse != null)
                    {
                        cachedResponse.AnalysisId = progressRequestId;
                        return Ok(cachedResponse);
                    }
                }

                // Return empty response with progress tracking
                return Ok(new AnalysisResponse
                {
                    AnalysisId = progressRequestId,
                    PrimaryKeyword = request.PrimaryKeyword,
                    TargetRegion = request.TargetRegion,
                    Targeturl = request.TargetUrl,
                    IsCompleted = false,
                    IsError = false
                });
            }

            // Mark analysis as started
            var analysisId = Guid.NewGuid().ToString();
            _cache.Set(cacheKeyStarted, true, TimeSpan.FromMinutes(30));
            _cache.Set(requestIdCacheKey, analysisId, TimeSpan.FromMinutes(30));

            // Report initial progress
            _progressReporter.Report(analysisId, 5, "initializing", "Starting analysis...");

            // Return immediately with initial response
            var initialResponse = new AnalysisResponse
            {
                AnalysisId = analysisId,
                PrimaryKeyword = request.PrimaryKeyword,
                TargetRegion = request.TargetRegion,
                Targeturl = request.TargetUrl,
                IsCompleted = false,
                IsError = false
            };

            // Start background processing without blocking the HTTP response
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _service.AnalyzeAsync(request, CancellationToken.None, analysisId);
                    result.AnalysisId = analysisId;
                    result.IsCompleted = true;
                    result.IsError = false;

                    // Cache the complete result
                    _cache.Set(cacheKey, System.Text.Json.JsonSerializer.Serialize(result), TimeSpan.FromMinutes(60));

                    // Report completion
                    _progressReporter.Report(analysisId, 100, "complete", "Analysis complete!", isCompleted: true);

                    // Store analysis
                    StoreAnalysis(request, result);
                }
                catch (Exception ex)
                {
                    _progressReporter.Report(analysisId, 0, "error", $"Analysis failed: {ex.Message}", isCompleted: true, isError: true, errorDetail: ex.Message);
                }
            }, CancellationToken.None);

            return Ok(initialResponse);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new AnalysisErrorResponse
            {
                Error = ex.Message,
                StackTrace = ex.StackTrace ?? ex.ToString()
            });
        }
    }

    [HttpGet("progress/{analysisId}")]
    public ActionResult<object> GetAnalysisProgress(string analysisId)
    {
        if (string.IsNullOrWhiteSpace(analysisId))
            return BadRequest("AnalysisId is required.");

        var progress = _progressReporter.Get(analysisId);
        if (progress == null)
        {
            return Ok(new
            {
                percentage = 0,
                stage = "pending",
                message = "Analysis has not started yet.",
                isCompleted = false,
                isError = false,
                errorDetail = (string)null,
                timestampUtc = DateTime.UtcNow.ToString("o")
            });
        }

        return Ok(new
        {
            percentage = progress.Percentage,
            stage = progress.Stage,
            message = progress.Message,
            isCompleted = progress.IsCompleted,
            isError = progress.IsError,
            errorDetail = progress.ErrorDetail,
            timestampUtc = progress.TimestampUtc.ToString("o")
        });
    }

    [HttpPost("generate-outline")]
    public async Task<ActionResult<GeneratedOutline>> GenerateOutline(
        [FromBody] GenerateOutlineRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AnalysisId) &&
            (string.IsNullOrWhiteSpace(request.PrimaryKeyword) ||
             string.IsNullOrWhiteSpace(request.CountryOrRegion) ||
             string.IsNullOrWhiteSpace(request.Language) ||
             string.IsNullOrWhiteSpace(request.WebsiteUrl)))
            return BadRequest("Provide either AnalysisId or PrimaryKeyword, CountryOrRegion, Language, and WebsiteUrl.");

        AnalysisResponse? analysis = null;
        if (!string.IsNullOrWhiteSpace(request.AnalysisId) &&
            _cache.TryGetValue(GetAnalysisCacheKeyById(request.AnalysisId), out AnalysisResponse? cachedAnalysis) &&
            cachedAnalysis is not null)
        {
            analysis = cachedAnalysis;
        }
        else if (!string.IsNullOrWhiteSpace(request.PrimaryKeyword) &&
                 !string.IsNullOrWhiteSpace(request.CountryOrRegion) &&
                 !string.IsNullOrWhiteSpace(request.Language) &&
                 !string.IsNullOrWhiteSpace(request.WebsiteUrl) &&
                 _cache.TryGetValue(GetAnalysisCacheKey(request.PrimaryKeyword, request.CountryOrRegion, request.Language, request.WebsiteUrl), out AnalysisResponse? legacyAnalysis) &&
                 legacyAnalysis is not null)
        {
            analysis = legacyAnalysis;
        }

        if (analysis is null)
            return NotFound("No cached analysis matches this request. Run /analyze first.");

        var availableCompetitorQuestions = analysis.Foundational.Top10
            .SelectMany(x => x.Questions)
            .Concat(analysis.Foundational.Competitors.SelectMany(x => x.Questions))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var availableAdditionalQuestions = analysis.Metrics.AdditionalQuestions.Gaps
            .Select(x => x.Question)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var competitorQuestions = Clean(request.SelectedCompetitorQuestions);
        var additionalQuestions = Clean(request.SelectedAdditionalQuestions);
        if (competitorQuestions.Count + additionalQuestions.Count == 0)
            return BadRequest("Select at least one competitor or additional question.");
        //if (competitorQuestions.Any(x => !availableCompetitorQuestions.Contains(x)))
        //    return BadRequest("SelectedCompetitorQuestions must contain only questions from the cached competitor analysis.");
        //if (additionalQuestions.Any(x => !availableAdditionalQuestions.Contains(x)))
        //    return BadRequest("SelectedAdditionalQuestions must contain only questions from the cached additional questions.");

        var deduplicated = new[] { competitorQuestions, additionalQuestions }.DeduplicateQuestions();
        var selectedCompetitorQuestions = deduplicated.Where(competitorQuestions.Contains).ToList();
        var selectedAdditionalQuestions = deduplicated.Where(additionalQuestions.Contains).ToList();

        try
        {
            var outline = await _gemini.GenerateOutlineAsync(
                analysis,
                selectedCompetitorQuestions,
                selectedAdditionalQuestions,
                request.UserInput,
                cancellationToken);
            return Ok(outline);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest, new AnalysisErrorResponse
            {
                Error = "The outline generation request was cancelled.",
                StackTrace = Environment.StackTrace
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new AnalysisErrorResponse
            {
                Error = ex.Message,
                StackTrace = ex.StackTrace ?? ex.ToString()
            });
        }
    }

    private void StoreAnalysis(AnalysisRequest request, AnalysisResponse analysis)
    {
        if (_analysisOptions.CacheDurationMinutes <= 0) return;

        var analysisId = string.IsNullOrWhiteSpace(analysis.AnalysisId)
            ? Guid.NewGuid().ToString("N")
            : analysis.AnalysisId;

        analysis.AnalysisId = analysisId;

        _cache.Set(GetAnalysisCacheKeyById(analysisId), analysis, TimeSpan.FromMinutes(_analysisOptions.CacheDurationMinutes));
        _cache.Set(GetAnalysisCacheKey(request.PrimaryKeyword, request.TargetRegion, request.Language, request.TargetUrl), analysis,
            TimeSpan.FromMinutes(_analysisOptions.CacheDurationMinutes));
    }

    private static string GetAnalysisCacheKeyById(string analysisId)
        => $"analysis:id:{Normalize(analysisId)}";

    private static List<string> Clean(IEnumerable<string>? questions) => questions?
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x.Trim())
        .ToList() ?? [];

    private static string GetAnalysisCacheKey(string keyword, string country, string language, string websiteUrl)
        => $"analysis:{Normalize(keyword)}:{Normalize(country)}:{Normalize(language)}:{NormalizeWebsiteUrl(websiteUrl)}";

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static string NormalizeWebsiteUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.Unescaped).TrimEnd('/').ToLowerInvariant()
            : Normalize(value).TrimEnd('/');

}
