using Amazon.Runtime.Internal;
using Azure;
using Azure.Core;
using Centauri_Api.Entitites;
using Centauri_Api.Interface;
using Centauri_Api.Model;
using CentauriSeo.Application.Pipeline;
using CentauriSeo.Application.Scoring;
using CentauriSeo.Application.Utils;
using CentauriSeo.Core.Models.Enums;
using CentauriSeo.Core.Models.Input;
using CentauriSeo.Core.Models.Output;
using CentauriSeo.Core.Models.Outputs;
using CentauriSeo.Core.Models.Scoring;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using CentauriSeo.Infrastructure.Exceptions;
using CentauriSeo.Infrastructure.LlmClients;
using CentauriSeo.Infrastructure.LlmDtos;
using CentauriSeo.Infrastructure.Logging;
using CentauriSeo.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Extensions;
using Stripe.Forwarding;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CentauriSeoBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SeoController : ControllerBase
{
    private readonly Phase1And2OrchestratorService _orchestrator;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly GroqClient _groqClient;
    private readonly IMemoryCache _cache;
    private readonly IDynamoDbService _dynamoDbService;
    private readonly ILogger<SeoController> _logger;
    private readonly ILlmLogger _llmLogger;
    private readonly IMockDataService _mockDataService;
    private readonly IRecommendationFeedbackService _feedbackService;

    public SeoController(Phase1And2OrchestratorService orchestrator, IHttpContextAccessor httpContextAccessor, GroqClient groqClient,
        IMemoryCache cache, IDynamoDbService dynamoDbService, ILogger<SeoController> logger, ILlmLogger llmLogger, IMockDataService mockDataService, IRecommendationFeedbackService feedbackService)
    {
        _orchestrator = orchestrator;
        _httpContextAccessor = httpContextAccessor;
        _groqClient = groqClient;
        _cache = cache;
        _dynamoDbService = dynamoDbService;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _llmLogger = llmLogger ?? throw new ArgumentNullException(nameof(llmLogger));
        _mockDataService = mockDataService ?? throw new ArgumentNullException(nameof(mockDataService));
        _feedbackService = feedbackService ?? throw new ArgumentNullException(nameof(feedbackService));
    }

    [HttpPost("categories-test")]
    public async Task<ActionResult<RecommendationResponseDTO>> TestCategories([FromBody] List<string> h2Tags)
    {
        var response = await _groqClient.GetGroqCategorization(h2Tags);
        return Ok(response);
    }

    [HttpPost("recommendations-test")]
    public async Task<ActionResult<RecommendationResponseDTO>> GetTestRecommendations([FromBody] SeoRequest request)
    {
        var fullLocalLlmTags = await GetFullSentenceTaggingFromLocalLLP(request.PrimaryKeyword, request.Article.Raw);
        var sections = Phase1And2OrchestratorService.BuildSections(fullLocalLlmTags.Sentences);
        var recommendations =await _orchestrator.GetFullRecommendationsAsync(request, fullLocalLlmTags.Sentences, sections);
        return Ok(recommendations);
    }

    [HttpPost("expertise")]
    public async Task<double> GetExpertiseScore([FromBody] SeoRequest request)
    {
        var fullLocalLlmTags = await GetFullSentenceTaggingFromLocalLLP(request.PrimaryKeyword, request.Article.Raw);
        var sections = Phase1And2OrchestratorService.BuildSections(fullLocalLlmTags.Sentences);
        var res = await _groqClient.AnalyzeArticleExpertise(request.Article.Raw);

        return _groqClient.CalculateArticleExpertiseScore(res,fullLocalLlmTags.Sentences);
    }

    [HttpPost("credibility")]
    public async Task<double> GetCredibilityScore([FromBody] SeoRequest request)
    {
        var fullLocalLlmTags = await GetFullSentenceTaggingFromLocalLLP(request.PrimaryKeyword, request.Article.Raw);
        return await GetCredibilityScoreFromSentences(fullLocalLlmTags.Sentences);
    }

    private async Task<double> GetCredibilityScoreFromSentences(List<GeminiSentenceTag> sentences)
    {
        int batchSize = 10;
        List<SentenceStrengthResponse> results = new List<SentenceStrengthResponse>();
        for (int i = 0; i < sentences.Count; i += batchSize)
        {
            var batchSentences = sentences.Skip(i).Take(batchSize).ToList();
            var res = await _groqClient.GetSentenceStrengths(batchSentences);
            if (res != null)
            {
                results.AddRange(res);
            }
        }

        IEnumerable<ValidatedSentence> validatedSentences = new List<ValidatedSentence>();
        sentences.ForEach(s =>
        {
            var strengthData = results?.Where(x => x.Sentence == s.Sentence)?.FirstOrDefault();
            if (strengthData != null)
            {
                validatedSentences = validatedSentences.Append(new ValidatedSentence()
                {
                    Text = s.Sentence,
                    InformativeType = s.InformativeType,
                    Structure = s.Structure,
                    Voice = s.Voice,
                    HtmlTag = s.HtmlTag,
                    HasCitation = s.ClaimsCitation,
                    IsGrammaticallyCorrect = s.IsGrammaticallyCorrect,
                    IsPlagiarized = s.IsPlagiarized,
                    AnswerSentenceFlag = s.AnswerSentenceFlag,
                    Strength = strengthData.Strength,
                });
            }
        });
        return CredibilityScorer.Score(validatedSentences);
    }
    private  async Task<double> GetExpertiseScore(string articleText, List<GeminiSentenceTag> sentences)
    {
        var res = await _groqClient.AnalyzeArticleExpertise(articleText);

        return _groqClient.CalculateArticleExpertiseScore(res, sentences);
    }

    [HttpPost("authority")]
    public async Task<double> GetAuthorityScore([FromBody] SeoRequest request)
    {
        var fullLocalLlmTags = await GetFullSentenceTaggingFromLocalLLP(request.PrimaryKeyword, request.Article.Raw);
        var sections = Phase1And2OrchestratorService.BuildSections(fullLocalLlmTags.Sentences);
        var res = await _groqClient.AnalyzeExpertise(sections);
        OrchestratorResponse orchestratorResponse = orchestratorResponse = await _orchestrator.RunAsync(request, fullLocalLlmTags);
        return AuthorityScorer.Score(orchestratorResponse.ValidatedSentences);
    }
    [Authorize]
    [HttpPost("analyze")]
    public async Task<ActionResult<SeoResponse>> Analyze([FromBody] SeoRequest request)
    {
        const string provider = "SeoController:Analyze";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _llmLogger.LogInfo($"Analyze endpoint called | PrimaryKeyword: {request?.PrimaryKeyword}", new Dictionary<string, object> { { "Endpoint", "Analyze" } });

            // Validate input
            if (request == null)
                throw new LlmValidationException("Request cannot be null", provider, new List<string> { "Invalid request" });

            if (string.IsNullOrWhiteSpace(request.PrimaryKeyword))
                throw new LlmValidationException("Primary keyword is required", provider, new List<string> { "PrimaryKeyword is required" });

            if (request.Article == null || string.IsNullOrWhiteSpace(request.Article.Raw))
                throw new LlmValidationException("Article content is required", provider, new List<string> { "Article is required" });

            // Check if mock mode is enabled - return mock data immediately if so
            if (_mockDataService.IsMockModeEnabled)
            {
                _llmLogger.LogInfo("📋 MOCK MODE: Returning mock analysis response");
                var mockResponse = await _mockDataService.GetMockAnalysisResponseAsync();
                if (mockResponse != null)
                {
                    mockResponse.RequestId = Guid.NewGuid().ToString();
                    stopwatch.Stop();
                    _llmLogger.LogApiCall(provider, "Analyze (Mock)", stopwatch.ElapsedMilliseconds, true);
                    return Ok(mockResponse);
                }
                else
                {
                    _llmLogger.LogWarning("Mock mode enabled but failed to load mock data");
                }
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
                throw new LlmOperationException("Unable to identify user", provider, "User context missing");

            var user = await _dynamoDbService.GetUserAsync(userId);
            if (user == null)
                throw new LlmOperationException("User not found", provider, userId);

            if (TrialEnded(user))
            {
                _llmLogger.LogWarning($"User trial ended | UserId: {userId}", new Dictionary<string, object> { { "TrialEnd", user.TrialEndsAt } });
                return Unauthorized(new { error = "Free trial ended for user. Please upgrade the subscription or add credits" });
            }

            if (user.CreditsAdded <= 0)
            {
                _llmLogger.LogWarning($"No credits available | UserId: {userId}", new Dictionary<string, object> { { "Credits", user.CreditsAdded } });
                return Unauthorized(new { error = "No credits left" });
            }

            var analyzeResponse = new SeoResponse();
            var cacheKey = $"analyze__{request.PrimaryKeyword}_{request.Article.Raw}";
            var cacheKey2 = $"analyze__{request.PrimaryKeyword}_{request.Article.Raw}_isAnalysisStarted";

            var cachedData = _cache.Get(cacheKey);
            var isAnalysisStarted = _cache.Get(cacheKey2);

            if (isAnalysisStarted != null)
            {
                _llmLogger.LogDebug($"Analysis already in progress | CacheKey: {cacheKey}");
                if (cachedData != null)
                {
                    stopwatch.Stop();
                    _llmLogger.LogApiCall(provider, "Analyze (Cached)", stopwatch.ElapsedMilliseconds, true);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
                    return Ok(JsonSerializer.Deserialize<SeoResponse>(cachedData?.ToString(), options));
                }
                return Ok(analyzeResponse);
            }

            _cache.Set(cacheKey2, true, TimeSpan.FromMinutes(15));
            stopwatch.Stop();
            _llmLogger.LogApiCall(provider, "Analyze (Initiated)", stopwatch.ElapsedMilliseconds, true);

            // Start background analysis
            _ = GetAnalysisResult(request, userId);
            return Ok(analyzeResponse);
        }
        catch (LlmValidationException valEx)
        {
            stopwatch.Stop();
            _llmLogger.LogWarning($"Validation error in analyze | {string.Join(", ", valEx.ValidationErrors)}", new Dictionary<string, object> { { "DurationMs", stopwatch.ElapsedMilliseconds } });
            _llmLogger.LogApiCall(provider, "Analyze", stopwatch.ElapsedMilliseconds, false, valEx.Message);
            return BadRequest(new { error = valEx.Message, validationErrors = valEx.ValidationErrors });
        }
        catch (LlmOperationException opEx)
        {
            stopwatch.Stop();
            _logger.LogError($"Operation error in analyze: {opEx.Message}");
            _llmLogger.LogApiCall(provider, "Analyze", stopwatch.ElapsedMilliseconds, false, opEx.Message);
            return StatusCode(500, new { error = "Analysis failed", details = opEx.Message });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, $"Unexpected error in analyze endpoint");
            _llmLogger.LogError($"Unexpected error in analyze | {ex.Message}", ex, new Dictionary<string, object> { { "DurationMs", stopwatch.ElapsedMilliseconds } });
            _llmLogger.LogApiCall(provider, "Analyze", stopwatch.ElapsedMilliseconds, false, ex.Message);

            return StatusCode(500, new SeoResponse()
            {
                IsCompleted = true,
                Error = "An unexpected error occurred during analysis"
            });
        }
    }

    private bool TrialEnded(CentauriUser? user)
    {
        bool trialEnded = false;
        try
        {
           if( user.TrialEndsAt< DateTime.UtcNow)
            {
                trialEnded = true;
            }
        }
        catch
        {

        }
        return trialEnded;
    }

    private async Task<SeoResponse> GetAnalysisResult(SeoRequest request, string userId)
    {
        const string provider = "SeoController:GetAnalysisResult";
        var operationStopwatch = Stopwatch.StartNew();

        try
        {
            _llmLogger.LogInfo($"Starting full analysis | UserId: {userId}", new Dictionary<string, object> { { "Keyword", request.PrimaryKeyword } });

            var cacheKey = $"analyze__{request.PrimaryKeyword}_{request.Article.Raw}";
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            var ctx = _httpContextAccessor.HttpContext;
            var correlationId = ctx?.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

            var response = new SeoResponse { RequestId = correlationId };
            var topIssues = new List<TopIssue>();

            // Validate input integrity
            response.InputIntegrity = EnsureInputIntegrity(request);

            // Get sentence tagging from local LLM
            var localTagStopwatch = Stopwatch.StartNew();
            var fullLocalLlmTags = await GetFullSentenceTaggingFromLocalLLP(request.PrimaryKeyword, request.Article.Raw);
            localTagStopwatch.Stop();
            _llmLogger.LogDebug($"Local LLM tagging completed | DurationMs: {localTagStopwatch.ElapsedMilliseconds}");

            if (fullLocalLlmTags?.Sentences == null || fullLocalLlmTags.Sentences.Count == 0)
            {
                throw new LlmOperationException("No sentences extracted from article", provider, request.PrimaryKeyword);
            }

            fullLocalLlmTags.Sentences?.RemoveAll(x => x.Sentence?.ToLower().Contains("meta title") == true
                || x.Sentence?.ToLower().Contains("meta description") == true
                || x.Sentence?.ToLower().Contains("url slug") == true);

            var sections = Phase1And2OrchestratorService.BuildSections(fullLocalLlmTags.Sentences);
            var headings = fullLocalLlmTags.Sentences
                .Where(s => s.HtmlTag?.ToLower() != "h1" && s.HtmlTag?.StartsWith("h", StringComparison.InvariantCultureIgnoreCase) == true)
                .Select(s => s.Sentence)
                .ToList();

            // Update informative types from Groq
            await UpdateInformativeTypeFromGroq(options, fullLocalLlmTags);

            // Run orchestrator
            var orchestratorStopwatch = Stopwatch.StartNew();
            OrchestratorResponse orchestratorResponse = await _orchestrator.RunAsync(request, fullLocalLlmTags);
            orchestratorStopwatch.Stop();
            _llmLogger.LogDebug($"Orchestrator completed | DurationMs: {orchestratorStopwatch.ElapsedMilliseconds}");

            if (orchestratorResponse?.ValidatedSentences == null || orchestratorResponse.ValidatedSentences.Count == 0)
            {
                throw new LlmOperationException("Orchestrator returned no validated sentences", provider, request.PrimaryKeyword);
            }

            orchestratorResponse.Sections = sections;

            // Convert to Level1 format
            var level1 = orchestratorResponse.ValidatedSentences.ToList().ConvertAll(x => new Level1Sentence()
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
            response.Level1.SentenceMap = orchestratorResponse.ValidatedSentences.Select(v => new SentenceMapEntry
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

            // Compute scores
            response.Level2InputResponse = orchestratorResponse;
            response.Request = request;
            var l2 = Level2Engine.Compute(request, orchestratorResponse);
            l2.ExpertiseScore = await GetExpertiseScore(request.Article.Raw, fullLocalLlmTags.Sentences);
            l2.CredibilityScore = await GetCredibilityScoreFromSentences(fullLocalLlmTags.Sentences);

            var l3 = Level3Engine.Compute(l2);
            var l4 = Level4Engine.Compute(l2, l3);

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
                    RelevanceScore = Math.Round(response.Level3Scores.RelevanceScore),
                    AuthorityScore = Math.Round(response.Level2Scores.AuthorityScore * 10),
                    ExpertiseScore = Math.Round(response.Level2Scores.ExpertiseScore * 10)
                }
            };

            // Get recommendations
            var recommendationStopwatch = Stopwatch.StartNew();
            var sectionResponse = await _orchestrator.GetSectionScoreResAsync(request.PrimaryKeyword);
            await _orchestrator.GetFullRecommendationsAsync(request, fullLocalLlmTags.Sentences, sections, l2, orchestratorResponse);
            recommendationStopwatch.Stop();
            _llmLogger.LogDebug($"Recommendations completed | DurationMs: {recommendationStopwatch.ElapsedMilliseconds}");

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

            response.InputIntegrity.Status = allPresent ? "success" : "partial";
            response.Status = allPresent ? "success" : "partial";
            response.IsCompleted = true;

            // Cache result
            var cacheStopwatch = Stopwatch.StartNew();
            _cache.Set(cacheKey, JsonSerializer.Serialize(response, options), TimeSpan.FromMinutes(15));
            cacheStopwatch.Stop();
            _llmLogger.LogDebug($"Result cached | DurationMs: {cacheStopwatch.ElapsedMilliseconds}");

            // Update user credits
            try
            {
                var user = await _dynamoDbService.GetUserAsync(userId);
                if (user != null)
                {
                    user.CreditsAdded -= 1;
                    await _dynamoDbService.UpdateUserAsync(user);
                    _llmLogger.LogDebug($"User credits updated | UserId: {userId} | Remaining: {user.CreditsAdded}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to update user credits for UserId: {userId}");
                _llmLogger.LogWarning($"Failed to deduct credit | UserId: {userId} | {ex.Message}");
            }

            operationStopwatch.Stop();
            _llmLogger.LogApiCall(provider, "GetAnalysisResult", operationStopwatch.ElapsedMilliseconds, true);
            return response;
        }
        catch (LlmOperationException opEx)
        {
            operationStopwatch.Stop();
            _logger.LogError($"Operation error in analysis: {opEx.Message}");
            _llmLogger.LogApiCall(provider, "GetAnalysisResult", operationStopwatch.ElapsedMilliseconds, false, opEx.Message);

            var errorResponse = new SeoResponse()
            {
                IsCompleted = true,
                Error = opEx.Message,
                Status = "error"
            };
            
            var cacheKey = $"analyze__{request.PrimaryKeyword}_{request.Article.Raw}";
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
            _cache.Set(cacheKey, JsonSerializer.Serialize(errorResponse, options), TimeSpan.FromMinutes(15));
            return errorResponse;
        }
        catch (Exception ex)
        {
            operationStopwatch.Stop();
            _logger.LogError(ex, $"Unexpected error in analysis for keyword: {request?.PrimaryKeyword}");
            _llmLogger.LogError($"Unexpected error in GetAnalysisResult | {ex.Message}", ex, new Dictionary<string, object> { { "DurationMs", operationStopwatch.ElapsedMilliseconds } });
            _llmLogger.LogApiCall(provider, "GetAnalysisResult", operationStopwatch.ElapsedMilliseconds, false, ex.Message);

            var errorResponse = new SeoResponse()
            {
                IsCompleted = true,
                Error = "An unexpected error occurred during analysis",
                Status = "error"
            };

            try
            {
                var cacheKey = $"analyze__{request.PrimaryKeyword}_{request.Article.Raw}";
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
                _cache.Set(cacheKey, JsonSerializer.Serialize(errorResponse, options), TimeSpan.FromMinutes(15));
            }
            catch { } // Ignore cache errors

            return errorResponse;
        }
    }

    private async Task UpdateInformativeTypeFromGroq(JsonSerializerOptions options, AiIndexinglevelLocalLlmResponse fullLocalLlmTags)
    {
        const string provider = "SeoController:UpdateInformativeTypeFromGroq";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (fullLocalLlmTags?.Sentences == null || fullLocalLlmTags.Sentences.Count == 0)
            {
                _llmLogger.LogDebug("No sentences to update informative type");
                return;
            }

            _llmLogger.LogDebug($"Updating informative types | SentenceCount: {fullLocalLlmTags.Sentences.Count}");

            var requestData = fullLocalLlmTags.Sentences
                .Select(x => new { Sentence = x.Sentence, InformativeType = x.InformativeType.GetDisplayName() })
                .Take(200)
                .ToList();

            var res = await _groqClient.UpdateInformativeType(JsonSerializer.Serialize(requestData));
            
            if (string.IsNullOrWhiteSpace(res))
            {
                _llmLogger.LogWarning("Groq returned empty response for informative type update");
                return;
            }

            var d = JsonSerializer.Deserialize<List<UpdatedData>>(res, options);
            if (d == null || d.Count == 0)
            {
                _llmLogger.LogWarning("Failed to deserialize Groq informative type response");
                return;
            }

            int updateCount = 0;
            fullLocalLlmTags.Sentences.ForEach(s =>
            {
                var groqData = d?.Where(x => x.Sentence == s.Sentence)?.FirstOrDefault();
                if (groqData != null && groqData.InformativeType != s.InformativeType)
                {
                    s.InformativeType = groqData.InformativeType;
                    updateCount++;
                }
            });

            stopwatch.Stop();
            _llmLogger.LogApiCall(provider, "UpdateInformativeType", stopwatch.ElapsedMilliseconds, true);
            _llmLogger.LogDebug($"Updated {updateCount} sentences with new informative types");
        }
        catch (JsonException jsonEx)
        {
            stopwatch.Stop();
            _llmLogger.LogWarning($"JSON parsing error in UpdateInformativeType | {jsonEx.Message}", new Dictionary<string, object> { { "DurationMs", stopwatch.ElapsedMilliseconds } });
            _logger.LogError(jsonEx, "Failed to parse Groq informative type response");
        }
        catch (LlmOperationException)
        {
            stopwatch.Stop();
            _llmLogger.LogWarning($"LLM operation error in UpdateInformativeType");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error in UpdateInformativeTypeFromGroq");
            _llmLogger.LogWarning($"Error updating informative types | {ex.Message}");
        }
    }

    [HttpPost("Level2Scores")]
    public Level2Scores GetLevel2Scores([FromBody] Level2ScoreRequest request)
    {
        if (request == null) return null;
        return Level2Engine.Compute(request.Request, request.OrchestratorResponse);
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
        const string provider = "SeoController:GetRecommendations";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };

            var correlationId = _httpContextAccessor?.HttpContext?.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

            // Basic input validation
            if (request == null || string.IsNullOrWhiteSpace(request.Article?.Raw))
            {
                return BadRequest("Invalid request: ArticleText is required.");
            }

            // Check if mock mode is enabled - return mock data immediately if so
            if (_mockDataService.IsMockModeEnabled)
            {
                _llmLogger.LogInfo("📋 MOCK MODE: Returning mock recommendations response");
                var mockResponse = await _mockDataService.GetMockRecommendationsResponseAsync();
                if (mockResponse != null)
                {
                    mockResponse.RequestId = correlationId;
                    stopwatch.Stop();
                    _llmLogger.LogApiCall(provider, "GetRecommendations (Mock)", stopwatch.ElapsedMilliseconds, true);
                    return Ok(mockResponse);
                }
                else
                {
                    _llmLogger.LogWarning("Mock mode enabled but failed to load mock recommendations data");
                }
            }

            // Generate recommendations using the orchestrator service
            var recommendations = await _orchestrator.GetRecommendationResponseAsync(request.Article.Raw);
            recommendations.RequestId = correlationId;

            stopwatch.Stop();
            _llmLogger.LogApiCall(provider, "GetRecommendations", stopwatch.ElapsedMilliseconds, true);
            return Ok(recommendations);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error in GetRecommendations");
            _llmLogger.LogError($"Error in GetRecommendations | {ex.Message}", ex, new Dictionary<string, object> { { "DurationMs", stopwatch.ElapsedMilliseconds } });
            return StatusCode(500, new { error = "Failed to generate recommendations", details = ex.Message });
        }
    }

    [HttpPost("recommendations/feedback")]
    [Authorize]
    public async Task<ActionResult<RecommendationFeedbackResponse>> SubmitRecommendationFeedback(
        [FromBody] RecommendationFeedbackRequest request
    )
    {
        const string provider = "SeoController:SubmitRecommendationFeedback";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _llmLogger.LogInfo($"📧 Receiving feedback submission for recommendation: {request?.RecommendationId}" );

            if (request == null)
            {
                throw new LlmValidationException("Feedback request cannot be null", provider, new List<string> { "Request body is empty" });
            }

            if (string.IsNullOrWhiteSpace(request.RecommendationId))
            {
                throw new LlmValidationException("RecommendationId is required", provider, new List<string> { "RecommendationId is missing" });
            }

            if (string.IsNullOrWhiteSpace(request.RequestId))
            {
                throw new LlmValidationException("RequestId is required", provider, new List<string> { "RequestId is missing" });
            }

            if (string.IsNullOrWhiteSpace(request.Feedback) || (request.Feedback != "up" && request.Feedback != "down"))
            {
                throw new LlmValidationException("Feedback must be 'up' or 'down'", provider, new List<string> { "Invalid feedback value" });
            }

            // Extract user information from claims/context
            var userId = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? "unknown";

            var userEmail = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? "unknown";

            var ipAddress = HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
            var userAgent = HttpContext?.Request?.Headers["User-Agent"].ToString() ?? "unknown";

            _llmLogger.LogDebug($"Feedback submission details | UserId: {userId}, RecommendationId: {request.RecommendationId}, Feedback: {request.Feedback}");

            // Submit feedback using the service
            var response = await _feedbackService.SubmitFeedbackAsync(
                userId,
                request,
                ipAddress,
                userAgent
            );

            stopwatch.Stop();
            _llmLogger.LogApiCall(provider, "SubmitRecommendationFeedback", stopwatch.ElapsedMilliseconds, true);

            if (response.Status == "success")
            {
                _logger.LogInformation(
                    "Feedback submitted successfully. FeedbackId: {FeedbackId}, RecommendationId: {RecommendationId}, UserId: {UserId}, Rating: {Rating}",
                    response.FeedbackId,
                    request.RecommendationId,
                    userId,
                    request.Feedback
                );
                return Ok(response);
            }
            else
            {
                _logger.LogWarning(
                    "Feedback submission returned error status. RecommendationId: {RecommendationId}, Error: {Error}",
                    request.RecommendationId,
                    response.ErrorDetails
                );
                return BadRequest(response);
            }
        }
        catch (LlmValidationException ex)
        {
            stopwatch.Stop();
            _llmLogger.LogError($"Validation error in feedback submission | {ex.Message}", ex);
            _logger.LogWarning("Feedback validation error: {Message}", ex.Message);
            return BadRequest(new
            {
                status = "error",
                message = "Validation failed",
                details = ex.Message,
                submittedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _llmLogger.LogError($"Error in SubmitRecommendationFeedback | {ex.Message}", ex, new Dictionary<string, object> { { "DurationMs", stopwatch.ElapsedMilliseconds } });
            _logger.LogError(ex, "Error submitting recommendation feedback");
            return StatusCode(500, new
            {
                status = "error",
                message = "Failed to submit feedback",
                details = ex.Message,
                submittedAt = DateTime.UtcNow
            });
        }
    }

    private async Task<AiIndexinglevelLocalLlmResponse> GetFullSentenceTaggingFromLocalLLP(string primaryKeyword, string htmlContent)
    {
        const string provider = "SeoController:GetFullSentenceTaggingFromLocalLLP";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(primaryKeyword))
                throw new LlmValidationException("Primary keyword is required", provider, new List<string> { "PrimaryKeyword required" });

            if (string.IsNullOrWhiteSpace(htmlContent))
                throw new LlmValidationException("HTML content is required", provider, new List<string> { "HtmlContent required" });

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };

            string apiUrl = "http://ec2-15-206-164-71.ap-south-1.compute.amazonaws.com:8000/process-article";

            var inputData = JsonSerializer.Serialize(new
            {
                htmlContent = htmlContent,
                primaryKeyword = primaryKeyword
            });

            _llmLogger.LogDebug($"Calling local LLP service | Keyword: {primaryKeyword}");

            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            {
                var content = new StringContent(inputData, System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync(apiUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    throw new LlmApiException(
                        "Local LLP service returned error",
                        provider,
                        (int?)response.StatusCode,
                        errorContent
                    );
                }

                var res = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(res))
                {
                    throw new LlmOperationException("Local LLP returned empty response", provider, primaryKeyword);
                }

                var results = JsonSerializer.Deserialize<AiIndexinglevelLocalLlmResponse>(res, options);
                
                if (results == null)
                {
                    throw new LlmParsingException("Failed to deserialize local LLP response", provider, res);
                }

                stopwatch.Stop();
                _llmLogger.LogApiCall(provider, "GetFullSentenceTaggingFromLocalLLP", stopwatch.ElapsedMilliseconds, true);
                return results;
            }
        }
        catch (HttpRequestException httpEx)
        {
            stopwatch.Stop();
            _logger.LogError(httpEx, "HTTP error calling local LLP service");
            throw new LlmApiException("Failed to call local LLP service", provider, null, httpEx.Message, httpEx);
        }
        catch (TaskCanceledException timeoutEx)
        {
            stopwatch.Stop();
            _logger.LogError(timeoutEx, "Local LLP service request timeout");
            throw new LlmTimeoutException("Local LLP service timeout", provider, TimeSpan.FromSeconds(30), timeoutEx);
        }
        catch (LlmOperationException)
        {
            stopwatch.Stop();
            throw; // Re-throw LLM exceptions
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, $"Unexpected error calling local LLP service for keyword: {primaryKeyword}");
            throw new LlmApiException("Unexpected error calling local LLP service", provider, null, ex.Message, ex);
        }
    }
    public class Level2ScoreRequest
    {
        public SeoRequest Request { get; set; }
        public OrchestratorResponse OrchestratorResponse { get; set; }
    }

    public class UpdatedData
    {
        public InformativeType InformativeType { get; set; }
        public string Sentence { get; set; }
    }
}
