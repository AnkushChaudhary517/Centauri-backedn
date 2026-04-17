using Azure;
using CentauriSeo.Application.Pipeline;
using CentauriSeo.Application.Scoring;
using CentauriSeo.Application.Services;
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
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CentauriSeo.Application.Pipeline;

public class Phase1And2OrchestratorService
{
    private readonly GroqClient _groq;
    private readonly GeminiClient _gemini;
    private readonly OpenAiClient _openAi;
    private readonly ILlmCacheService _cache;
    private readonly ILlmLogger _llmLogger;
    private readonly ILogger<Phase1And2OrchestratorService> _logger;

    public Phase1And2OrchestratorService(
        GroqClient groq,
        GeminiClient gemini,
        OpenAiClient openAi,
        ILlmCacheService cache,
        ILlmLogger llmLogger,
        ILogger<Phase1And2OrchestratorService> logger)
    {
        _groq = groq ?? throw new ArgumentNullException(nameof(groq));
        _gemini = gemini ?? throw new ArgumentNullException(nameof(gemini));
        _openAi = openAi ?? throw new ArgumentNullException(nameof(openAi));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _llmLogger = llmLogger ?? throw new ArgumentNullException(nameof(llmLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OrchestratorResponse> RunAsync(SeoRequest request, AiIndexinglevelLocalLlmResponse fullLocalLlmTags)
    {
        const string provider = "Phase1And2OrchestratorService:RunAsync";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _llmLogger.LogInfo($"Orchestrator started | Keyword: {request?.PrimaryKeyword}", new Dictionary<string, object> { { "Provider", provider } });

            if (request == null)
                throw new LlmValidationException("Request cannot be null", provider, new List<string> { "SeoRequest is required" });

            if (fullLocalLlmTags?.Sentences == null || fullLocalLlmTags.Sentences.Count == 0)
                throw new LlmValidationException("Sentences are required", provider, new List<string> { "Sentences cannot be empty" });

            // Get section scores
            var sectionScoreStopwatch = Stopwatch.StartNew();
            var sectionScoreResponse = await GetSectionScoreResAsync(request.PrimaryKeyword);
            sectionScoreStopwatch.Stop();
            _llmLogger.LogDebug($"Section scores retrieved | DurationMs: {sectionScoreStopwatch.ElapsedMilliseconds}");

            var sectionScore = await GetSectionScoreInfo(sectionScoreResponse, request.PrimaryKeyword, request.SecondaryKeywords, fullLocalLlmTags.Sentences);
            var intentScoreTask = GetIntentScoreInfo(sectionScoreResponse);
            var keywordScoreTask = GetKeywordScore(sectionScoreResponse, fullLocalLlmTags.Sentences, request);

            var sentences = fullLocalLlmTags.Sentences
                .Select(s => new Sentence(s.SentenceId, s.Sentence, s.ParagraphId))
                .ToList();

            // Get Gemini tags
            var geminiTagStopwatch = Stopwatch.StartNew();
            var geminiTags = await _gemini.TagArticleAsync(
                SentenceTaggingPrompts.GeminiSentenceTagPrompt,
                JsonSerializer.Serialize(sentences),
                "gemini:tagging"
            );
            geminiTagStopwatch.Stop();
            _llmLogger.LogDebug($"Gemini tagging completed | DurationMs: {geminiTagStopwatch.ElapsedMilliseconds}");

            if (geminiTags == null || geminiTags.Count == 0)
            {
                _llmLogger.LogWarning("Gemini returned no tags, using local tags only");
                geminiTags = new List<GeminiSentenceTag>();
            }

            // Merge tags
            MergeGeminiWithLocalTags(geminiTags, fullLocalLlmTags.Sentences);

            var anyMismatch = DetectMismatches(geminiTags, fullLocalLlmTags.Sentences);
            if (anyMismatch?.Count > 0)
            {
                _llmLogger.LogDebug($"Detected {anyMismatch.Count} mismatches between Gemini and local tags");
            }

            // Handle mismatches (currently disabled)
            List<ChatgptGeminiSentenceTag> chatGptDecisions = new List<ChatgptGeminiSentenceTag>();
            bool enableHandleMismatch = false;
            if (enableHandleMismatch && anyMismatch?.Count > 0)
            {
                var tasks = new List<Task<List<ChatgptGeminiSentenceTag>>>();
                var batchSize = 150;
                for (int i = 0; i < anyMismatch.Count; i += batchSize)
                {
                    tasks.Add(HandleMismatchSentences(
                        anyMismatch.Skip(i).Take(batchSize).ToList(),
                        geminiTags,
                        fullLocalLlmTags.Sentences,
                        chatGptDecisions
                    ));
                }

                await Task.WhenAll(tasks);

                foreach (var t in tasks)
                {
                    var d = await t;
                    if (d?.Count > 0)
                    {
                        chatGptDecisions.AddRange(d);
                    }
                }
            }

            // Run orchestrator validation
            var validated = new Phase1And2Orchestrator().Execute(sentences, fullLocalLlmTags.Sentences, geminiTags, chatGptDecisions);

            if (validated == null || validated.Count == 0)
            {
                _llmLogger.LogWarning("Orchestrator validation returned no results");
                validated = new List<ValidatedSentence>();
            }

            // Complete scoring tasks
            await Task.WhenAll(intentScoreTask, keywordScoreTask);
            var intentScore = await intentScoreTask;
            var keywordScore = await keywordScoreTask;

            var answerPositionIndex = fullLocalLlmTags.AnswerPositionIndex ?? await GetAnswerPositionIndex(validated?.ToList(), request);

            stopwatch.Stop();
            _llmLogger.LogApiCall(provider, "RunAsync", stopwatch.ElapsedMilliseconds, true);

            return new OrchestratorResponse()
            {
                ValidatedSentences = validated,
                SectionScoreResponse = sectionScoreResponse,
                SectionScore = sectionScore,
                IntentScore = intentScore,
                KeywordScore = keywordScore,
                AnswerPositionIndex = answerPositionIndex
            };
        }
        catch (LlmValidationException valEx)
        {
            stopwatch.Stop();
            _llmLogger.LogWarning($"Validation error in RunAsync | {string.Join(", ", valEx.ValidationErrors)}", new Dictionary<string, object> { { "DurationMs", stopwatch.ElapsedMilliseconds } });
            throw;
        }
        catch (LlmOperationException opEx)
        {
            stopwatch.Stop();
            _logger.LogError($"Operation error in orchestrator: {opEx.Message}");
            _llmLogger.LogError($"Operation error in RunAsync | {opEx.Message}", opEx, new Dictionary<string, object> { { "DurationMs", stopwatch.ElapsedMilliseconds } });
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, $"Unexpected error in RunAsync for keyword: {request?.PrimaryKeyword}");
            _llmLogger.LogError($"Unexpected error in RunAsync | {ex.Message}", ex, new Dictionary<string, object> { { "DurationMs", stopwatch.ElapsedMilliseconds } });
            throw new LlmOperationException("Orchestration failed", provider, $"Keyword: {request?.PrimaryKeyword}", ex);
        }
    }

    private static void MergeGeminiWithLocalTags(IReadOnlyList<GeminiSentenceTag>? geminiTags, List<GeminiSentenceTag> localSentences)
    {
        if (geminiTags == null || localSentences == null || localSentences.Count == 0)
            return;

        // Build a dictionary to avoid repeated FirstOrDefault lookups
        var localById = new Dictionary<string, GeminiSentenceTag>(localSentences.Count);
        foreach (var s in localSentences)
        {
            if (s?.SentenceId != null && !localById.ContainsKey(s.SentenceId))
                localById[s.SentenceId] = s;
        }

        foreach (var g in geminiTags)
        {
            if (g?.Sentence == null) continue;
            if (g.SentenceId != null && localById.TryGetValue(g.SentenceId, out var sentence))
            {
                // preserve original behavior but avoid extra null checks/allocations
                g.Sentence = sentence.Sentence ?? string.Empty;
                g.Structure = sentence.Structure;
                g.Voice = sentence.Voice;
                g.ClaimsCitation = sentence.ClaimsCitation;
                g.IsGrammaticallyCorrect = sentence.IsGrammaticallyCorrect;
                g.HasPronoun = sentence.HasPronoun;
                g.FunctionalType = sentence.FunctionalType;
                g.InfoQuality = sentence.InfoQuality;
                g.ClaritySynthesisType = sentence.ClaritySynthesisType;
                g.HtmlTag = sentence.HtmlTag;
                g.Source = sentence.Source;
            }
        }
    }

    private static List<GeminiSentenceTag> DetectMismatches(IReadOnlyList<GeminiSentenceTag>? geminiTags, List<GeminiSentenceTag> localSentences)
    {
        if (geminiTags == null || geminiTags.Count == 0) return new List<GeminiSentenceTag>();
        if (localSentences == null || localSentences.Count == 0) return geminiTags.ToList();

        // Create a quick lookup by sentence text to speed comparisons and avoid repeated enumeration
        var localBySentence = new Dictionary<string, GeminiSentenceTag>(localSentences.Count);
        foreach (var s in localSentences)
        {
            if (s?.Sentence == null) continue;
            if (!localBySentence.ContainsKey(s.Sentence))
                localBySentence[s.Sentence] = s;
        }

        var mismatches = new List<GeminiSentenceTag>();
        foreach (var gm in geminiTags)
        {
            if (gm == null)
            {
                mismatches.Add(gm);
                continue;
            }

            localBySentence.TryGetValue(gm.Sentence, out var gq);
            if (gq == null)
            {
                mismatches.Add(gm);
                continue;
            }

            if (gq.InformativeType != gm.InformativeType)
            {
                mismatches.Add(gm);
            }
        }

        return mismatches;
    }

    public static List<Section> BuildSections(List<GeminiSentenceTag> sentences)
    {
        var sections = new List<Section>();
        Section currentSection = null;
        int sectionCounter = 1;

        // Small optimization: use a HashSet for header comparisons
        var headerSet = new HashSet<string>(new[] { "h2", "h3", "h4" }, System.StringComparer.OrdinalIgnoreCase);
        bool IsHeader(string tag) => tag != null && headerSet.Contains(tag);

        foreach (var sentence in sentences)
        {
            if (IsHeader(sentence.HtmlTag))
            {
                if (currentSection != null)
                {
                    sections.Add(currentSection);
                }

                currentSection = new Section
                {
                    Id = $"S{sectionCounter++}",
                    SectionText = sentence.Sentence,
                    Sentences = new List<string>()
                };
            }
            else
            {
                if (currentSection != null)
                {
                    currentSection.Sentences.Add(sentence.Sentence);
                }
            }
        }

        if (currentSection != null)
        {
            sections.Add(currentSection);
        }

        return sections;
    }

    public List<Level1Sentence> FilterIrrelevantSentences(List<ValidatedSentence> localAnalysis, string primaryKeyword)
    {
        var relevantList = new List<Level1Sentence>();
        if (localAnalysis == null || localAnalysis.Count == 0 || string.IsNullOrEmpty(primaryKeyword))
            return relevantList;

        // Use IndexOf with OrdinalIgnoreCase to avoid allocating lowercased strings
        foreach (var s in localAnalysis)
        {
            bool hasDirectKeyword = !string.IsNullOrEmpty(s.Text) && s.Text.IndexOf(primaryKeyword, System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (s.RelevanceScore < 0.5 && !hasDirectKeyword)
            {
                continue;
            }

            if (s.InformativeType == Core.Models.Enums.InformativeType.Filler) continue;

            relevantList.Add(new Level1Sentence { Id = s.Id, Text = s.Text });
        }

        return relevantList;
    }

    private async Task<AnswerPositionIndex> GetAnswerPositionIndex(List<ValidatedSentence>? validatedSentences, SeoRequest request)
    {
        const string provider = "Phase1And2OrchestratorService:GetAnswerPositionIndex";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (validatedSentences == null || validatedSentences.Count == 0)
            {
                _llmLogger.LogDebug("No validated sentences provided, returning zero position score");
                return new AnswerPositionIndex() { FirstAnswerSentenceId = null, PositionScore = 0.0 };
            }

            if (request == null || string.IsNullOrWhiteSpace(request.PrimaryKeyword))
            {
                throw new LlmValidationException("Request and primary keyword are required", provider, new List<string> { "Invalid request" });
            }

            var entities = new List<string>();
            var aiIndexingResponse = new AiIndexinglevel1Response();
            var cacheKey = $"GetLevel1InforForAIIndexingResponse:{request.PrimaryKeyword}:Entities";

            // Get AI indexing info
            var relevantSentences = FilterIrrelevantSentences(validatedSentences, request.PrimaryKeyword);
            if (relevantSentences.Count == 0)
            {
                _llmLogger.LogDebug("No relevant sentences found for AI indexing");
                return new AnswerPositionIndex() { FirstAnswerSentenceId = null, PositionScore = 0.0 };
            }

            var aiIndexingStopwatch = Stopwatch.StartNew();
            var resList = await _gemini.GetLevel1InforForAIIndexing(request.PrimaryKeyword, relevantSentences, 100);
            aiIndexingStopwatch.Stop();
            _llmLogger.LogDebug($"AI indexing info retrieved | DurationMs: {aiIndexingStopwatch.ElapsedMilliseconds} | ResponseCount: {resList?.Count ?? 0}");

            if (resList != null && resList.Count > 0)
            {
                resList.ForEach(res =>
                {
                    try
                    {
                        var deserialized = JsonSerializer.Deserialize<AiIndexinglevel1Response>(res, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (deserialized?.Sentences != null && deserialized.Sentences.Count > 0)
                        {
                            aiIndexingResponse.Sentences.AddRange(deserialized.Sentences);
                        }

                        if (deserialized?.Entities != null && deserialized.Entities.Count > 0)
                        {
                            entities.AddRange(deserialized.Entities);
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogWarning(jsonEx, "Failed to parse AI indexing response item");
                        _llmLogger.LogDebug($"Skipped unparseable AI indexing response");
                    }
                });
            }

            // Cache entities if found
            if (entities.Count > 0)
            {
                try
                {
                    await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(entities.Distinct().ToList()));
                    _llmLogger.LogDebug($"Entities cached | Count: {entities.Distinct().Count()}");
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "Failed to cache entities");
                }
            }

            // Update validated sentences with AI indexing data
            validatedSentences.ForEach(vs =>
            {
                if (vs.InformativeType == Core.Models.Enums.InformativeType.Fact ||
                    vs.InformativeType == Core.Models.Enums.InformativeType.Claim ||
                    vs.InformativeType == Core.Models.Enums.InformativeType.Definition)
                {
                    var s = aiIndexingResponse.Sentences.FirstOrDefault(ds => ds.Id == vs.Id);
                    if (s != null)
                    {
                        vs.AnswerSentenceFlag = s.AnswerSentenceFlag;
                        vs.EntityConfidenceFlag = s.EntityConfidenceFlag;
                        vs.EntityMentionFlag = s.EntityMentionFlag ?? new EntityMentionFlag() { Entities = null, EntityCount = 0, Value = 0 };
                    }
                    else
                    {
                        vs.AnswerSentenceFlag = 0;
                        vs.EntityMentionFlag = new EntityMentionFlag() { Entities = null, EntityCount = 0, Value = 0 };
                        vs.EntityConfidenceFlag = 0;
                    }
                }
                else
                {
                    vs.AnswerSentenceFlag = 0;
                    vs.EntityMentionFlag = new EntityMentionFlag() { Entities = null, EntityCount = 0, Value = 0 };
                    vs.EntityConfidenceFlag = 0;
                }
            });

            // Calculate answer position score
            var firstAnswerSentenceId = aiIndexingResponse.AnswerPositionIndex?.FirstAnswerSentenceId;
            if (string.IsNullOrWhiteSpace(firstAnswerSentenceId))
            {
                _llmLogger.LogDebug("No first answer sentence found");
                stopwatch.Stop();
                _llmLogger.LogApiCall(provider, "GetAnswerPositionIndex", stopwatch.ElapsedMilliseconds, true);
                return new AnswerPositionIndex() { FirstAnswerSentenceId = null, PositionScore = 0.0 };
            }

            var firstAnswerSentence = validatedSentences.FirstOrDefault(x => x.Id == firstAnswerSentenceId);
            var positionIndex = validatedSentences.IndexOf(firstAnswerSentence);

            if (positionIndex < 0)
            {
                _llmLogger.LogDebug($"First answer sentence not found in validated list");
                stopwatch.Stop();
                _llmLogger.LogApiCall(provider, "GetAnswerPositionIndex", stopwatch.ElapsedMilliseconds, true);
                return new AnswerPositionIndex() { FirstAnswerSentenceId = firstAnswerSentenceId, PositionScore = 0.0 };
            }

            var percent = ((double)positionIndex / validatedSentences.Count) * 100;
            var finalAnswer = percent switch
            {
                <= 5 => 1.0,
                <= 10 => 0.75,
                <= 20 => 0.5,
                <= 30 => 0.25,
                _ => 0.0
            };

            stopwatch.Stop();
            _llmLogger.LogApiCall(provider, "GetAnswerPositionIndex", stopwatch.ElapsedMilliseconds, true);
            _llmLogger.LogDebug($"Answer position calculated | Position: {positionIndex}/{validatedSentences.Count} ({percent:F1}%) | Score: {finalAnswer}");

            return new AnswerPositionIndex() { FirstAnswerSentenceId = firstAnswerSentenceId, PositionScore = finalAnswer };
        }
        catch (LlmValidationException)
        {
            stopwatch.Stop();
            _llmLogger.LogWarning($"Validation error in GetAnswerPositionIndex", new Dictionary<string, object> { { "DurationMs", stopwatch.ElapsedMilliseconds } });
            return new AnswerPositionIndex() { FirstAnswerSentenceId = null, PositionScore = 0.0 };
        }
        catch (LlmOperationException opEx)
        {
            stopwatch.Stop();
            _logger.LogWarning(opEx, "Operation error in GetAnswerPositionIndex");
            _llmLogger.LogWarning($"Operation error in GetAnswerPositionIndex | {opEx.Message}", new Dictionary<string, object> { { "DurationMs", stopwatch.ElapsedMilliseconds } });
            return new AnswerPositionIndex() { FirstAnswerSentenceId = null, PositionScore = 0.0 };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error in GetAnswerPositionIndex");
            _llmLogger.LogError($"Error in GetAnswerPositionIndex | {ex.Message}", ex, new Dictionary<string, object> { { "DurationMs", stopwatch.ElapsedMilliseconds } });
            return new AnswerPositionIndex() { FirstAnswerSentenceId = null, PositionScore = 0.0 };
        }
    }

    private async Task<List<ChatgptGeminiSentenceTag>?> HandleMismatchSentences(List<GeminiSentenceTag>? mismatchedSentences, IReadOnlyList<GeminiSentenceTag>? geminiTags, IReadOnlyList<GeminiSentenceTag>? localTags, List<ChatgptGeminiSentenceTag>? chatGptDecisions)
    {
        const string provider = "Phase1And2OrchestratorService:HandleMismatchSentences";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (mismatchedSentences == null || mismatchedSentences.Count == 0)
            {
                _llmLogger.LogDebug("No mismatched sentences to handle");
                return new List<ChatgptGeminiSentenceTag>();
            }

            _llmLogger.LogDebug($"Handling {mismatchedSentences.Count} mismatched sentences");

            var mismatchSentences = mismatchedSentences
                .Select(x => new { Sentenceid = x.SentenceId, Sentence = x.Sentence })
                .ToList();

            var prompt = JsonSerializer.Serialize(new
            {
                SystemRequirement = SentenceTaggingPrompts.ChatGptTagPromptConcise,
                UserContent = mismatchSentences
            });

            var cacheKey = _cache.ComputeRequestKey(prompt, "Chatgpt:Arbitration");
            var cached = await _cache.GetAsync(cacheKey);
            var done = false;
            var exception = string.Empty;
            var retryCount = 0;

            while (!done && retryCount < 1)
            {
                try
                {
                    string aiRaw;
                    if (cached != null)
                    {
                        _llmLogger.LogDebug("Using cached arbitration response");
                        aiRaw = cached;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(exception))
                        {
                            prompt += $"Exception : this error occured in previous call. Do not repeat the error again and fix the response. : {exception}.";
                        }
                        
                        var arbitrationStopwatch = Stopwatch.StartNew();
                        aiRaw = await _openAi.CompleteAsync(prompt);
                        arbitrationStopwatch.Stop();
                        _llmLogger.LogDebug($"OpenAI arbitration completed | DurationMs: {arbitrationStopwatch.ElapsedMilliseconds}");
                    }

                    if (string.IsNullOrWhiteSpace(aiRaw))
                    {
                        throw new LlmOperationException("OpenAI returned empty arbitration response", provider, $"MismatchCount: {mismatchedSentences.Count}");
                    }

                    var options = new JsonSerializerOptions
                    {
                        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
                        PropertyNameCaseInsensitive = true
                    };

                    if (chatGptDecisions != null && chatGptDecisions.Any())
                    {
                        var res = JsonSerializer.Deserialize<ChatGptResponse>(aiRaw, options);
                        if (res != null && res.Choices != null)
                        {
                            var content = res.Choices.FirstOrDefault()?.Message?.Content;
                            if (!string.IsNullOrWhiteSpace(content))
                            {
                                var decisions = JsonSerializer.Deserialize<List<ChatgptGeminiSentenceTag>>(content, options);
                                if (decisions != null && decisions.Count > 0)
                                {
                                    chatGptDecisions.AddRange(decisions);
                                    
                                    try
                                    {
                                        await _cache.SaveAsync(cacheKey, aiRaw);
                                        _llmLogger.LogDebug("Arbitration result cached");
                                    }
                                    catch (Exception cacheEx)
                                    {
                                        _logger.LogWarning(cacheEx, "Failed to cache arbitration result");
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        var res = JsonSerializer.Deserialize<ChatGptResponse>(aiRaw, options);
                        var content = res?.Choices?.FirstOrDefault()?.Message?.Content;
                        chatGptDecisions = JsonSerializer.Deserialize<List<ChatgptGeminiSentenceTag>>(content, options);
                        
                        if (chatGptDecisions != null)
                        {
                            try
                            {
                                await _cache.SaveAsync(cacheKey, aiRaw);
                                _llmLogger.LogDebug("Arbitration result cached");
                            }
                            catch (Exception cacheEx)
                            {
                                _logger.LogWarning(cacheEx, "Failed to cache arbitration result");
                            }
                        }
                    }

                    done = true;
                    stopwatch.Stop();
                    _llmLogger.LogApiCall(provider, "HandleMismatchSentences", stopwatch.ElapsedMilliseconds, true);
                }
                catch (JsonException jsonEx)
                {
                    exception = jsonEx.Message;
                    retryCount++;
                    _logger.LogWarning(jsonEx, $"JSON parsing error in arbitration (attempt {retryCount})");
                    _llmLogger.LogDebug($"JSON parsing error in arbitration");
                }
                catch (Exception ex)
                {
                    exception = ex.Message;
                    retryCount++;
                    _logger.LogWarning(ex, $"Error in arbitration (attempt {retryCount})");
                    _llmLogger.LogDebug($"Error in arbitration | {ex.Message}");
                }
            }

            return chatGptDecisions;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error in HandleMismatchSentences");
            _llmLogger.LogError($"Error in HandleMismatchSentences | {ex.Message}", ex, new Dictionary<string, object> { { "DurationMs", stopwatch.ElapsedMilliseconds } });
            return new List<ChatgptGeminiSentenceTag>();
        }
    }

    public async Task<double> GetKeywordScore(SectionScoreResponse response, List<GeminiSentenceTag> validated, SeoRequest request)
    {
        var h1 = validated.FirstOrDefault(vs => string.Equals(vs.HtmlTag, "h1", System.StringComparison.OrdinalIgnoreCase))?.Sentence ?? string.Empty;
        var h2 = validated.Where(vs => string.Equals(vs.HtmlTag, "h2", System.StringComparison.OrdinalIgnoreCase))?.Select(x => x.Sentence);
        var h3 = validated.Where(vs => string.Equals(vs.HtmlTag, "h3", System.StringComparison.OrdinalIgnoreCase))?.Select(x => x.Sentence);
        var body = string.Concat(validated.Where(vs => !string.Equals(vs.HtmlTag, "h1", System.StringComparison.OrdinalIgnoreCase) && !string.Equals(vs.HtmlTag, "h2", System.StringComparison.OrdinalIgnoreCase) && !string.Equals(vs.HtmlTag, "h3", System.StringComparison.OrdinalIgnoreCase)).Select(x => x.Sentence)) ?? string.Empty;

        var h2h3list = new List<string>();
        if (h2 != null)
            h2h3list.AddRange(h2);
        if (h3 != null)
            h2h3list.AddRange(h3);

        return await KeywordScorer.CalculateKeywordScore(request.PrimaryKeyword, request.SecondaryKeywords, response.Variants, new ContentData()
        {
            H1 = h1,
            MetaDescription = request.MetaDescription,
            MetaTitle = request.MetaTitle,
            UrlSlug = request.Url,
            HeadersH2H3 = h2h3list,
            RawBodyText = body
        });
    }

    public async Task<double> GetIntentScoreInfo(SectionScoreResponse response)
    {
        int match = 0;
        if (response?.Competitors == null || response.Competitors.Count == 0)
        {
            return 0.0;
        }
        foreach (var c in response.Competitors)
        {
            if (c.Intent == response.Intent)
                match += 1;
        }
        var intentScore = ((double)match / response.Competitors.Count);
        return intentScore * 10;
    }
    public async Task<SectionScoreResponse> GetSectionScoreResAsync(string keyword)
    {
        const string provider = "Phase1And2OrchestratorService:GetSectionScoreResAsync";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(keyword))
                throw new LlmValidationException("Keyword is required", provider, new List<string> { "Keyword cannot be null or empty" });

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };

            var cacheKey = _cache.ComputeRequestKey(keyword, "SectionScores");
            
            // Try cache first
            try
            {
                var cached = await _cache.GetAsync(cacheKey);
                if (!string.IsNullOrWhiteSpace(cached))
                {
                    stopwatch.Stop();
                    _llmLogger.LogApiCall(provider, "GetSectionScore (Cached)", stopwatch.ElapsedMilliseconds, true);
                    _llmLogger.LogDebug($"Section score retrieved from cache | Keyword: {keyword}");
                    return JsonSerializer.Deserialize<SectionScoreResponse>(cached, options);
                }
            }
            catch (Exception cacheEx)
            {
                _logger.LogWarning(cacheEx, "Cache retrieval failed, proceeding with API call");
                _llmLogger.LogDebug($"Cache miss for section scores | Keyword: {keyword}");
            }

            // Call API with retry
            var retryCount = 0;
            const int maxRetries = 2;
            Exception lastException = null;

            while (retryCount < maxRetries)
            {
                try
                {
                    var res = await _gemini.GetSectionScore(keyword);
                    
                    if (string.IsNullOrWhiteSpace(res))
                    {
                        throw new LlmOperationException("Gemini returned empty section score response", provider, keyword);
                    }

                    var sectionScores = JsonSerializer.Deserialize<SectionScoreResponse>(res, options);
                    
                    if (sectionScores == null)
                    {
                        throw new LlmParsingException("Failed to deserialize section scores", provider, res);
                    }

                    // Save to cache
                    try
                    {
                        await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(sectionScores));
                        _llmLogger.LogDebug("Section scores cached");
                    }
                    catch (Exception cacheEx)
                    {
                        _logger.LogWarning(cacheEx, "Failed to cache section scores");
                    }

                    stopwatch.Stop();
                    _llmLogger.LogApiCall(provider, "GetSectionScore", stopwatch.ElapsedMilliseconds, true);
                    return sectionScores;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    retryCount++;
                    
                    if (retryCount < maxRetries)
                    {
                        _logger.LogWarning(ex, $"Section score retrieval failed (attempt {retryCount}/{maxRetries}), retrying...");
                        _llmLogger.LogDebug($"Retrying GetSectionScore | Attempt: {retryCount}/{maxRetries}");
                        await Task.Delay(1000 * retryCount); // Exponential backoff
                    }
                }
            }

            stopwatch.Stop();
            _logger.LogError(lastException, $"Failed to get section scores after {maxRetries} attempts for keyword: {keyword}");
            _llmLogger.LogApiCall(provider, "GetSectionScore", stopwatch.ElapsedMilliseconds, false, lastException?.Message);
            
            throw new LlmApiException("Failed to retrieve section scores", provider, null, lastException?.Message, lastException);
        }
        catch (LlmValidationException)
        {
            stopwatch.Stop();
            throw; // Re-throw validation errors
        }
        catch (LlmOperationException)
        {
            stopwatch.Stop();
            throw; // Re-throw operation errors
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, $"Unexpected error in GetSectionScoreResAsync for keyword: {keyword}");
            throw new LlmApiException("Unexpected error getting section scores", provider, null, ex.Message, ex);
        }
    }
    public async Task<double> GetSectionScoreInfo(SectionScoreResponse response, string keyword, List<string> secondaryKeywords, List<GeminiSentenceTag> validatedSentences)
    {
        var options = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };
        List<string> myHeadings = validatedSentences.Where(vs => string.Equals(vs.HtmlTag, "h2", System.StringComparison.OrdinalIgnoreCase)).Select(x => x.Sentence).ToList();

        var cHeadings = new List<string>();
        response.Competitors?.ForEach(c =>
        {
            cHeadings.AddRange(c.Headings);
        });
        cHeadings.AddRange(myHeadings);
        var batchSize = 30;
        var finalRes = new List<string>();
        for (int i = 0; i < cHeadings.Count; i += batchSize)
        {
            var data = await _groq.GetGroqCategorization(cHeadings);
            if (data != null && data.Count > 0)
                finalRes.AddRange(data);
        }

        return SectionScorer.Calculate(response?.Competitors, keyword, finalRes);
    }

    public async Task<RecommendationResponseDTO> GetRecommendationResponseAsync(string article)
    {
        var options = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };
        var cacheKey = _cache.ComputeRequestKey(article, "GeminiRecommendations:Complete");
        var cached = await _cache.GetAsync(cacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            return JsonSerializer.Deserialize<RecommendationResponseDTO>(cached, options);
        }
        return new RecommendationResponseDTO()
        {
            Recommendations = new RecommendationsResponse(),
            Status = "NotStarted"
        };
    }

    public async Task<RecommendationResponseDTO> GetFullRecommendationsAsync(SeoRequest seoRequest, List<GeminiSentenceTag> level1, List<Section> sections,
        Level2Scores level2Scores = null, OrchestratorResponse orchestratorResponse = null, RecommendationResponseDTO previousRecommendations = null)
     {
        const string provider = "Phase1And2OrchestratorService:GetFullRecommendationsAsync";
        var stopwatch = Stopwatch.StartNew();

        var response = new RecommendationResponseDTO()
        {
            Recommendations = new RecommendationsResponse(),
            Status = "InProgress"
        };

        try
        {
            if (seoRequest == null)
                throw new LlmValidationException("SeoRequest is required", provider, new List<string> { "SeoRequest is null" });

            var options = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
            var cacheKey = _cache.ComputeRequestKey(seoRequest.Article.Raw, "GeminiRecommendations:Complete");

            // Try cache first
            try
            {
                var cached = await _cache.GetAsync(cacheKey);
                if (!string.IsNullOrEmpty(cached))
                {
                    stopwatch.Stop();
                    _llmLogger.LogDebug($"Recommendations retrieved from cache | DurationMs: {stopwatch.ElapsedMilliseconds}");
                    var recommendationRes = JsonSerializer.Deserialize<RecommendationResponseDTO>(cached, options);
                    return recommendationRes ?? response;
                }
            }
            catch (Exception cacheEx)
            {
                _logger.LogWarning(cacheEx, "Cache retrieval failed for recommendations");
            }

            _llmLogger.LogInfo($"Generating recommendations | Keyword: {seoRequest.PrimaryKeyword}");

            var cacheEntitiesKey = $"GetLevel1InforForAIIndexingResponse:{seoRequest.PrimaryKeyword}:Entities";
            var cachedEntities = await _cache.GetAsync(cacheEntitiesKey);

            var request = JsonSerializer.Serialize(new
            {
                PrimaryKeyword = seoRequest.PrimaryKeyword,
                Sections = sections?.Select(x => new
                {
                    SectionId = x.Id,
                    SectionText = x.SectionText,
                    Sentences = x.Sentences
                }).ToList(),
                Scores = level2Scores,
                SearchIntent = orchestratorResponse?.SectionScoreResponse != null ? Enum.GetName(orchestratorResponse.SectionScoreResponse.Intent) : null,
                Sentences = level1.Select(x => new
                {
                    SentenceId = x.SentenceId,
                    Text = x.Sentence,
                    HtmlTag = x.HtmlTag,
                }).ToList(),
                previousRecommendations = previousRecommendations?.Recommendations
            }, options);

            var genStopwatch = Stopwatch.StartNew();
            response.Recommendations = await GenerateRecommendationsAsync(request);
            genStopwatch.Stop();
            _llmLogger.LogDebug($"Recommendations generated | DurationMs: {genStopwatch.ElapsedMilliseconds}");

            response.Status = "Completed";

            // Cache result
            try
            {
                await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(response));
                _llmLogger.LogDebug("Recommendations cached");
            }
            catch (Exception cacheEx)
            {
                _logger.LogWarning(cacheEx, "Failed to cache recommendations");
            }

            // Also cache result by RequestId so reanalyze can fetch it
            try
            {
                if (!string.IsNullOrWhiteSpace(seoRequest?.RequestId))
                {
                    var requestIdCacheKey = _cache.ComputeRequestKey(seoRequest.RequestId, "GeminiRecommendations:ByRequest");
                    await _cache.SaveAsync(requestIdCacheKey, JsonSerializer.Serialize(response));
                    _llmLogger.LogDebug("Recommendations cached by RequestId");
                }
            }
            catch { }

            stopwatch.Stop();
            _llmLogger.LogApiCall(provider, "GetFullRecommendationsAsync", stopwatch.ElapsedMilliseconds, true);
            return response;
        }
        catch (LlmValidationException valEx)
        {
            stopwatch.Stop();
            _llmLogger.LogWarning($"Validation error in recommendations | {string.Join(", ", valEx.ValidationErrors)}", new Dictionary<string, object> { { "DurationMs", stopwatch.ElapsedMilliseconds } });
            response.Status = "ValidationError";
            return response;
        }
        catch (LlmOperationException opEx)
        {
            stopwatch.Stop();
            _logger.LogError(opEx, "Operation error in GetFullRecommendationsAsync");
            _llmLogger.LogError($"Operation error in recommendations | {opEx.Message}", opEx, new Dictionary<string, object> { { "DurationMs", stopwatch.ElapsedMilliseconds } });
            response.Status = "Error";
            
            // Cache partial result if available
            if (response?.Recommendations?.Overall?.Count > 0)
            {
                try
                {
                    var cacheKey = _cache.ComputeRequestKey(seoRequest?.Article?.Raw, "GeminiRecommendations:Complete");
                    await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(response));
                }
                catch { }
            }
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, $"Unexpected error in GetFullRecommendationsAsync for keyword: {seoRequest?.PrimaryKeyword}");
            _llmLogger.LogError($"Unexpected error in recommendations | {ex.Message}", ex, new Dictionary<string, object> { { "DurationMs", stopwatch.ElapsedMilliseconds } });
            response.Status = "Error";

            if (response?.Recommendations?.Overall?.Count > 0)
            {
                try
                {
                    var cacheKey = _cache.ComputeRequestKey(seoRequest?.Article?.Raw, "GeminiRecommendations:Complete");
                    await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(response));
                }
                catch { }
            }
            return response;
        }
    }

    public async Task<RecommendationsResponse> GenerateRecommendationsAsync(string article)
    {
        const string provider = "Phase1And2OrchestratorService:GenerateRecommendationsAsync";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(article))
                throw new LlmValidationException("Article is required", provider, new List<string> { "Article cannot be null or empty" });

            var options = new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            };

            var cacheKey = _cache.ComputeRequestKey(article, "GeminiRecommendations");

            // Try cache
            try
            {
                var cached = await _cache.GetAsync(cacheKey);
                if (!string.IsNullOrEmpty(cached))
                {
                    stopwatch.Stop();
                    _llmLogger.LogDebug($"Recommendations retrieved from cache | DurationMs: {stopwatch.ElapsedMilliseconds}");
                    return JsonSerializer.Deserialize<RecommendationsResponse>(cached, options) ?? new RecommendationsResponse();
                }
            }
            catch (Exception cacheEx)
            {
                _logger.LogWarning(cacheEx, "Cache retrieval failed for recommendations");
            }

            _llmLogger.LogDebug($"Calling Gemini for recommendations");

            var genStopwatch = Stopwatch.StartNew();
           
            var response = await _gemini.GenerateRecommendationsAsync(article);
            genStopwatch.Stop();
            _llmLogger.LogDebug($"Gemini response received | DurationMs: {genStopwatch.ElapsedMilliseconds}");

            if (string.IsNullOrWhiteSpace(response))
            {
                throw new LlmOperationException("Gemini returned empty recommendations response", provider, "GenerateRecommendationsAsync");
            }

            var recommendations = JsonSerializer.Deserialize<RecommendationsResponse>(response, options);

            if (recommendations == null)
            {
                throw new LlmParsingException("Failed to deserialize recommendations response", provider, response);
            }

            // Cache result
            try
            {
                await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(recommendations));
                _llmLogger.LogDebug("Recommendations cached");
            }
            catch (Exception cacheEx)
            {
                _logger.LogWarning(cacheEx, "Failed to cache recommendations");
            }

            stopwatch.Stop();
            _llmLogger.LogApiCall(provider, "GenerateRecommendationsAsync", stopwatch.ElapsedMilliseconds, true);
            return recommendations;
        }
        catch (LlmValidationException)
        {
            stopwatch.Stop();
            throw; // Re-throw validation errors
        }
        catch (LlmOperationException)
        {
            stopwatch.Stop();
            throw; // Re-throw operation errors
        }
        catch (JsonException jsonEx)
        {
            stopwatch.Stop();
            _logger.LogError(jsonEx, "JSON parsing error in GenerateRecommendationsAsync");
            throw new LlmParsingException("Failed to parse recommendations response", provider, article, jsonEx);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error in GenerateRecommendationsAsync");
            _llmLogger.LogError($"Unexpected error generating recommendations | {ex.Message}", ex, new Dictionary<string, object> { { "DurationMs", stopwatch.ElapsedMilliseconds } });
            throw new LlmApiException("Failed to generate recommendations", provider, null, ex.Message, ex);
        }
    }
}