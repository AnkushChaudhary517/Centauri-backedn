using Centauri.ContentArchitect.Backend.Models;
using Centauri.ContentArchitect.Backend.Services;
using Centauri.ContentArchitect.Backend.Configuration;
using Centauri.ContentArchitect.Backend.Extensions;
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

    public AnalysisController(IContentArchitectService service, IGeminiClient gemini, IMemoryCache cache, IOptions<AnalysisOptions> analysisOptions)
    {
        _service = service;
        _gemini = gemini;
        _cache = cache;
        _analysisOptions = analysisOptions.Value;
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
            var result = await _service.AnalyzeAsync(request, cancellationToken);
            //var test = System.IO.File.ReadAllText("AnalysisResponse.json");
            //var result = JsonConvert.DeserializeObject<AnalysisResponse>(test);
            StoreAnalysis(request, result);
            return Ok(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest, new AnalysisErrorResponse
            {
                Error = "The analysis request was cancelled.",
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
