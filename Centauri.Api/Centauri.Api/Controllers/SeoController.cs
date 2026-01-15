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

namespace CentauriSeoBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SeoController : ControllerBase
{
    private readonly Phase1And2OrchestratorService _orchestrator;

    public SeoController(Phase1And2OrchestratorService orchestrator)
    {
        _orchestrator = orchestrator;
    }

    [HttpPost("analyze")]
    public async Task<ActionResult<SeoResponse>> Analyze([FromBody] SeoRequest request)
    {
        var response = new SeoResponse
        {
            RequestId = Guid.NewGuid().ToString()
        };
        var topIssues = new List<TopIssue>();

        // --- Input integrity initialization ---
        var input = response.InputIntegrity;
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
        input.Received.MetaDescription = request.MetaDescription;
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
            response.Status = "failed";
            input.Status = "failed";
            if (!input.MissingInputs.Contains("article"))
                input.MissingInputs.Add("article");
            input.Messages.Add("Article missing: processing halted.");
            return BadRequest(response);
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

       

        // --- Phase1/2 orchestration (deterministic stub if orchestrator not available) ---
        OrchestratorResponse orchestratorResponse = null;
        //IReadOnlyList<ValidatedSentence> validated = null;
        try
        {
            orchestratorResponse = await _orchestrator.RunAsync(request);

        }
        catch(Exception ex)
        {
            await (new FileLogger()).LogErrorAsync($"Error occured in analyze :  {ex.Message}:{ex.StackTrace}");
            //validated = level1.Select(l => new ValidatedSentence
            //{
            //    Id = l.Id,
            //    Text = l.Text,
            //    Grammar = l.IsGrammaticallyCorrect ? "correct" : "incorrect",
            //    InformativeType = l.InformativeType,
            //    Structure = l.Structure,
            //    Voice = l.Voice,
            //    HasCitation = l.HasCitation,
            //    Confidence = 1.0
            //}).ToList();
        }
        // --- Phase 0 + Level 1 ---
        //var l1 = Level1Engine.Analyze(request.Article);

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
        var l2 = Level2Engine.Compute(request, orchestratorResponse?.ValidatedSentences);

        l2.PlagiarismScore = orchestratorResponse?.PlagiarismScore ?? 1.0;
        l2.SectionScore =   orchestratorResponse?.SectionScore ?? 1.0;
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
                AiIndexingScore = Math.Round(response.Level4Scores.AiIndexingScore),
                SeoScore = Math.Round(response.Level4Scores.CentauriSeoScore),
                EeatScore = Math.Round(response.Level3Scores.EeatScore),
                ReadabilityScore = Math.Round(response.Level3Scores.ReadabilityScore * 10),
                RelevanceScore = Math.Round(response.Level3Scores.RelevanceScore)
            }
        };

        response.Diagnostics = new Diagnostics()
        {
            SkippedDueToMissingInputs = input.SkippedChecks,
            TopIssues = topIssues 
        };
        // Populate recommended quick diagnostics/recommendations (legacy)
        
        int offset = 100;
        for (int i = 0; i < level1.Count; i+=offset)
        {
            var chunk = level1.Skip(i).Take(offset).ToList();
            var level1Sentences = string.Join(" ", chunk.Select(s => s.Text));
            response.Recommendations.AddRange(await _orchestrator.GenerateRecommendationsAsync(level1Sentences, l2, l3, l4));

        }
        //response.Recommendations = TextAnalysisHelper.GenerateRecommendations(l2, l3, l4).ToList();

        // --- Final input_integrity.status per document rules ---
        bool allPresent = input.Received.ArticlePresent
                          && input.Received.PrimaryKeywordPresent
                          && input.Received.MetaTitlePresent
                          && input.Received.MetaDescriptionPresent
                          && input.Received.UrlPresent;

        if (!allPresent)
        {
            input.Status = "partial";
            response.Status = "partial";
        }
        else
        {
            input.Status = "success";
            response.Status = "success";
        }

        return Ok(response);
    }
}
