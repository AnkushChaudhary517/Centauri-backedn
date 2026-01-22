using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using CentauriSeo.Core.Models.Input;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Output;
using CentauriSeo.Application.Pipeline;
using CentauriSeo.Application.Scoring;
using CentauriSeo.Core.Models.Utilities;
using CentauriSeo.Application.Utils;
using CentauriSeo.Core.Models.Outputs;
using CentauriSeo.Infrastructure.Logging;
using System.Text.Json;
using Centauri_Api.Model;

namespace CentauriSeoBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SeoController : ControllerBase
{
    private readonly Phase1And2OrchestratorService _orchestrator;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SeoController(Phase1And2OrchestratorService orchestrator, IHttpContextAccessor httpContextAccessor)
    {
        _orchestrator = orchestrator;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpPost("analyze")]
    public async Task<ActionResult<SeoResponse>> Analyze([FromBody] SeoRequest request)
    {
        try
        {
            var ctx = _httpContextAccessor.HttpContext;
            var correlationId = ctx?.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
            var response = new SeoResponse
            {
                RequestId = correlationId
            };
            var topIssues = new List<TopIssue>();
            response.InputIntegrity = EnsureInputIntegrity(request);

            OrchestratorResponse orchestratorResponse = orchestratorResponse = await _orchestrator.RunAsync(request);

            //start recommendations
            _orchestrator.GetFullRecommendationsAsync(request.Article.Raw, orchestratorResponse?.ValidatedSentences?.ToList(), orchestratorResponse?.Sections);

            var level1 = orchestratorResponse?.ValidatedSentences?.ToList()?.ConvertAll(x => new Level1Sentence()
            {
                HasCitation = x.HasCitation,
                HasPronoun = x.HasPronoun,
                Id = x.Id,
                InfoQuality = x.InfoQuality,
                InformativeType = x.InformativeType,
                IsGrammaticallyCorrect = x.IsGrammaticallyCorrect,
                IsPlagiarized = x.IsPlagiarized,
                Structure = x.Structure,
                Text = x.Text
            });


            response.Level1.Summary.SentenceCount = level1.Count;

            response.Level1.Summary.StructureDistribution = level1
                .GroupBy(s => s.Structure.ToString())
                .ToDictionary(g => g.Key.ToLowerInvariant(), g => g.Count());

            response.Level1.Summary.InformativeTypeDistribution = level1
                .GroupBy(s => s.InformativeType.ToString())
                .ToDictionary(g => g.Key.ToLowerInvariant(), g => g.Count());

            response.Level1.Summary.CitationDistribution = new Dictionary<string, int>
            {
                ["with_citation"] = level1.Count(s => s.HasCitation),
                ["without_citation"] = level1.Count(s => !s.HasCitation)
            };

            response.Level1.Summary.GrammarDistribution = new Dictionary<string, int>
            {
                ["correct"] = level1.Count(s => s.IsGrammaticallyCorrect),
                ["incorrect"] = level1.Count(s => !s.IsGrammaticallyCorrect)
            };

            response.Level1.SentenceMapIncluded = true;
            response.Level1.SentenceMap = orchestratorResponse?.ValidatedSentences?.Select(v => new SentenceMapEntry
            {
                Id = v.Id,
                Text = v.Text,
                FinalTags = new FinalTags
                {
                    InformativeType = v.InformativeType.ToString().ToLowerInvariant(),
                    Citation = v.HasCitation ? "with_citation" : "without_citation",
                    Structure = v.Structure.ToString().ToLowerInvariant(),
                    Voice = v.Voice.ToString().ToLowerInvariant(),
                    Grammar = v.Grammar
                }
            }).ToList();

            // --- Compute scores (scorers will internally respect missing primary_keyword where required) ---
            var l2 = Level2Engine.Compute(request, orchestratorResponse);

            l2.PlagiarismScore = orchestratorResponse?.PlagiarismScore ?? 1.0;
            l2.SectionScore = orchestratorResponse?.SectionScore ?? 1.0;
            l2.AuthorityScore *= 10;
            var l3 = Level3Engine.Compute(l2);
            var l4 = Level4Engine.Compute(l2, l3);

            // Populate simplified final blocks (detailed population done later in response shaping)
            response.Level2Scores = l2;
            response.Level3Scores = l3;
            response.Level4Scores = l4;
            response.SeoScore = CentauriSeoScorer.Score(l2, l3, l4);

            response.FinalScores = new FinalScores()
            {
                UserVisible = new UserVisibleFinal()
                {
                    AiIndexingScore = Math.Round(response.Level4Scores.AiIndexingScore * 10),
                    SeoScore = Math.Round(response.Level4Scores.CentauriSeoScore),
                    EeatScore = Math.Round(response.Level3Scores.EeatScore),
                    ReadabilityScore = Math.Round(response.Level3Scores.ReadabilityScore * 10),
                    RelevanceScore = Math.Round(response.Level3Scores.RelevanceScore)
                }
            };

            response.Diagnostics = new Diagnostics()
            {
                SkippedDueToMissingInputs = response?.InputIntegrity?.SkippedChecks,
                TopIssues = topIssues
            };


            bool allPresent = response.InputIntegrity.Received.ArticlePresent
                              && response.InputIntegrity.Received.PrimaryKeywordPresent
                              && response.InputIntegrity.Received.MetaTitlePresent
                              && response.InputIntegrity.Received.MetaDescriptionPresent
                              && response.InputIntegrity.Received.UrlPresent;

            if (!allPresent)
            {
                response.InputIntegrity.Status = "partial";
                response.Status = "partial";
            }
            else
            {
                response.InputIntegrity.Status = "success";
                response.Status = "success";
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            await (new FileLogger()).LogErrorAsync($"Error occured in analyze :  {ex.Message}:{ex.StackTrace}");
        }
        return StatusCode(500, "Internal server error occurred during analysis.");
    }

    private InputIntegrity EnsureInputIntegrity(SeoRequest request)
    {

        // --- Input integrity initialization ---
        InputIntegrity input = new InputIntegrity();
        input.Received = input.Received ?? new ReceivedInputs();

        // Article presence (raw mandatory)
        bool articlePresent = request?.Article != null && !string.IsNullOrWhiteSpace(request.Article.Raw);
        input.Received.ArticlePresent = articlePresent;

        // Primary keyword presence (required for keyword-dependent logic)
        bool primaryPresent = !string.IsNullOrWhiteSpace(request?.PrimaryKeyword);
        input.Received.PrimaryKeywordPresent = primaryPresent;

        // Secondary keywords: default to empty list if null (no penalty)
        if (request?.SecondaryKeywords == null)
        {
            request!.SecondaryKeywords = new List<string>();
            input.DefaultsApplied["secondary_keywords_defaulted_to_empty"] = true;
        }
        input.Received.SecondaryKeywordsPresent = request.SecondaryKeywords != null;

        // Meta title/description/url presence and URL validity
        input.Received.MetaTitlePresent = !string.IsNullOrWhiteSpace(request?.MetaTitle);
        input.Received.MetaTitle = request.MetaTitle;
        input.Received.MetaDescriptionPresent = !string.IsNullOrWhiteSpace(request?.MetaDescription);
        input.Received.MetaDescription = request.MetaDescription;
        input.Received.UrlPresent = !string.IsNullOrWhiteSpace(request.Url);
        input.Received.Url = request.Url;
        input.Received.PrimaryKeyword = request.PrimaryKeyword;
        input.Received.SecondaryKeywords = (request.SecondaryKeywords != null) ? JsonSerializer.Serialize(request.SecondaryKeywords) : null;

        bool urlPresent = !string.IsNullOrWhiteSpace(request?.Url);
        bool urlValid = false;
        if (urlPresent)
        {
            if (Uri.TryCreate(request!.Url, UriKind.Absolute, out var parsed) &&
                (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
            {
                urlValid = true;
            }
            else
            {
                // invalid URL treated as missing per spec
                input.InvalidInputs.Add("url");
                input.Messages.Add("URL present but invalid: treated as missing and slug checks skipped.");
                urlPresent = false;
            }
        }
        input.Received.UrlPresent = urlPresent;

        // Context defaulting
        if (request?.Context == null)
        {
            input.DefaultsApplied["locale_defaulted"] = true;
        }

        // Article format default (if missing or empty)
        if (request?.Article != null && string.IsNullOrWhiteSpace(request.Article.Format))
        {
            input.DefaultsApplied["article_format_defaulted"] = true;
        }

        // --- Early failure if article missing ---
        if (!articlePresent)
        {
            input.Status = "failed";
            if (!input.MissingInputs.Contains("article"))
                input.MissingInputs.Add("article");
            input.Messages.Add("Article missing: processing halted.");
        }

        // --- Skipped checks & missing inputs for optional fields per spec ---
        if (!input.Received.MetaTitlePresent)
        {
            if (!input.MissingInputs.Contains("meta_title"))
                input.MissingInputs.Add("meta_title");
            input.SkippedChecks.Add("meta_title_keyword_checks");
            input.Messages.Add("Meta Title missing: title-based checks not evaluated.");
        }

        if (!input.Received.MetaDescriptionPresent)
        {
            if (!input.MissingInputs.Contains("meta_description"))
                input.MissingInputs.Add("meta_description");
            input.SkippedChecks.Add("meta_description_keyword_checks");
            input.Messages.Add("Meta Description missing: description-based checks not evaluated.");
        }

        if (!input.Received.UrlPresent)
        {
            if (!input.MissingInputs.Contains("url"))
                input.MissingInputs.Add("url");
            input.SkippedChecks.Add("url_slug_keyword_checks");
            input.Messages.Add("URL missing or invalid: slug checks not evaluated.");
        }

        if (!primaryPresent)
        {
            if (!input.MissingInputs.Contains("primary_keyword"))
                input.MissingInputs.Add("primary_keyword");
            input.SkippedChecks.Add("keyword_presence");
            input.SkippedChecks.Add("intent_alignment");
            input.SkippedChecks.Add("section_coverage");
            input.Messages.Add("Primary keyword missing: keyword-dependent checks skipped.");
        }

        return input;
    }

    [HttpPost("recommendations")]
    public async Task<ActionResult<RecommendationResponseDTO>> GetRecommendations([FromBody] SeoRequest request)
    {
        var correlationId = _httpContextAccessor?.HttpContext?.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
        // Basic input validation
        if (request == null || string.IsNullOrWhiteSpace(request.Article.Raw))
        {
            return BadRequest("Invalid request: ArticleText is required.");
        }
        // Generate recommendations using the TextAnalysisHelper
        var recommendations = await _orchestrator.GetRecommendationResponseAsync(request.Article.Raw);
        recommendations.RequestId = correlationId;
        return Ok(recommendations);
    }
}
