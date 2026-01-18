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
using CentauriSeo.Infrastructure.LlmClients;
using CentauriSeo.Infrastructure.LlmDtos;
using CentauriSeo.Infrastructure.Logging;
using CentauriSeo.Infrastructure.Services;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CentauriSeo.Application.Pipeline;

public class Phase1And2OrchestratorService
{
    private readonly GroqClient _groq;
    private readonly GeminiClient _gemini;
    private readonly OpenAiClient _openAi; // used for ChatGPT arbitration
    private readonly ILlmCacheService _cache;
    private readonly FileLogger _logger;

    public Phase1And2OrchestratorService(
        GroqClient groq,
        GeminiClient gemini,
        OpenAiClient openAi,
        ILlmCacheService cache)
    {
        _groq = groq;
        _gemini = gemini;
        _openAi = openAi;
        _cache = cache;
        _logger = new FileLogger();
    }

    // Runs Groq + Gemini tagging, detects mismatches, asks OpenAI (ChatGPT) for arbitration when needed,
    // then returns the validated sentence map using the existing Phase2_ArbitrationEngine.Execute flow.
    public async Task<OrchestratorResponse> RunAsync(SeoRequest request)
    {
        //var sentences = Phase0_InputParser.Parse(article);
        var sentenceTagging = (await _gemini.TagArticleAsync(SentenceTaggingPrompts.SentenceTaggingPrompt, request.Article.Raw, "gemini:tagging:level1")).ToList();
        var sentences = sentenceTagging?.Select(s => new Sentence(s.SentenceId, s.Sentence,0) ).ToList();
        var userContent = JsonSerializer.Serialize(sentences);


        var geminiTask = _gemini.TagArticleAsync(SentenceTaggingPrompts.GeminiSentenceTagPrompt, userContent, "gemini:tagging");
        var groqTask = _groq.TagSentencesAsync(userContent,string.Empty);       
        await Task.WhenAll(geminiTask, groqTask);


        var geminiTags = await geminiTask;
        geminiTags?.ToList()?.ForEach(g =>
        {
            var sentence = sentenceTagging.FirstOrDefault(s => s.SentenceId == g.SentenceId);
            g.Sentence = sentence?.Sentence ?? string.Empty;
            g.HtmlTag = sentence?.HtmlTag ?? string.Empty;
        });

        var groqTags = await groqTask;
        groqTags?.ToList()?.ForEach(g =>
        {
            var sentence = sentenceTagging.FirstOrDefault(s => s.SentenceId == g.SentenceId);
            g.Sentence = sentence?.Sentence ?? string.Empty;
            g.HtmlTag = sentence?.HtmlTag ?? string.Empty;
        });
        // 2) Detect mismatches (informative type OR citation flag OR voice)
        var anyMismatch = geminiTags?.Where(gm =>
        {
            var gq = groqTags.FirstOrDefault(x => x.Sentence == gm.Sentence);
            //var gm = geminiTags.SingleOrDefault(x => x.SentenceId == s.Id);
            if (gq == null || gm == null) return true;
            if (gq.InformativeType != gm.InformativeType) return true;
            if (gq.ClaimsCitation != gm.ClaimsCitation) return true;
            // voice/structure mismatches handled by Gemini mostly; ignore deterministic detector differences
            return false;
        }).ToList();

        List<ChatGptDecision>? chatGptDecisions = new List<ChatGptDecision>();
       
        if (anyMismatch != null && anyMismatch.Count>0)
        {

            var tasks = new List<Task<List<ChatGptDecision>>>();
            var batchSize = 50;
            for (int i=0;i< anyMismatch.Count; i+=batchSize)
            {
                tasks.Add(HandleMismatchSentences(sentences, geminiTags, groqTags, chatGptDecisions, batchSize, i));               
            }
            await Task.WhenAll(tasks);
            tasks.ForEach(async t =>
            {
                var d = await t;
                if(d != null && d.Count>0)
                {
                    chatGptDecisions.AddRange(d);
                }
            });
        }


        // 3) Run arbitration engine to produce validated sentences TODO: pass Groq tags when implemented

        var validated = new Phase1And2Orchestrator().Execute(sentences, groqTags, geminiTags, chatGptDecisions);
        return new OrchestratorResponse()
        {
            ValidatedSentences = validated,
            PlagiarismScore = await _gemini.GetPlagiarismScore(sentences),
            SectionScore = await GetSectionScoreInfo(request.PrimaryKeyword, validated?.ToList()),
            IntentScore = await GetIntentScoreInfo(request.PrimaryKeyword),
            KeywordScore = await GetKeywordScore(validated?.ToList(), request),
            AnswerPositionIndex = await GetAnswerPositionIndex(validated?.ToList(), request)
        };
    }

    private async Task<AnswerPositionIndex> GetAnswerPositionIndex(List<ValidatedSentence>? validatedSentences, SeoRequest request)
    {
        if (validatedSentences == null || validatedSentences.Count == 0)
            return new AnswerPositionIndex()
            {
                FirstAnswerSentenceId = null,
                PositionScore = 0.0
            };
        try
        {
            var res = await _gemini.GetLevel1InforForAIIndexing(request.PrimaryKeyword, validatedSentences.Select(vs => new Level1Sentence()
            {
                Id = vs.Id,
                Text = vs.Text
            }).ToList());

            var deserialized = JsonSerializer.Deserialize<AiIndexinglevel1Response>(res, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (deserialized != null)
            {
                validatedSentences.ForEach(vs =>
                {
                    if(vs.InformativeType == Core.Models.Enums.InformativeType.Fact ||
                            vs.InformativeType == Core.Models.Enums.InformativeType.Claim ||
                            vs.InformativeType == Core.Models.Enums.InformativeType.Definition)
                    {
                        var s = deserialized.Sentences.FirstOrDefault(ds => ds.Id == vs.Id && ds.Text == vs.Text);
                        if (s != null)
                        {
                            vs.AnswerSentenceFlag = s.AnswerSentenceFlag;
                            vs.EntityConfidenceFlag = s.EntityConfidenceFlag;
                            vs.EntityMentionFlag = s.EntityMentionFlag;
                        }
                        else
                        {

                        }
                    }
                    else
                    {
                        vs.AnswerSentenceFlag = new AnswerSentenceFlag() { Value=0,Reason=string.Empty };
                        vs.EntityMentionFlag = new EntityMentionFlag() { Entities = null, EntityCount =0, Value = 0 };
                        vs.EntityConfidenceFlag = new EntityConfidenceFlag() { Value = 0 };
                    }
                        

                });
                var pScore = validatedSentences.IndexOf(validatedSentences.Where(x => x.Id == deserialized.AnswerPositionIndex.FirstAnswerSentenceId).FirstOrDefault());
                var percent = (pScore / validatedSentences.Count)*100;
                switch(percent)
                {
                    case <= 5:
                        deserialized.AnswerPositionIndex.PositionScore = 1;
                        break;
                    case <= 10:
                        deserialized.AnswerPositionIndex.PositionScore = 0.75;
                        break;
                    case <= 20:
                        deserialized.AnswerPositionIndex.PositionScore = 0.5;
                        break;
                    case <= 30:
                        deserialized.AnswerPositionIndex.PositionScore = .25;
                        break;
                    default:
                        deserialized.AnswerPositionIndex.PositionScore = 0.0;
                        break;
                }
                return deserialized?.AnswerPositionIndex;
            }
        }
        catch(Exception ex)
        {
            await _logger.LogErrorAsync($"Error occured in getting answer position index : {ex.Message}:{ex.StackTrace}");
        }

        return new AnswerPositionIndex() {FirstAnswerSentenceId = null, PositionScore = 0.0 };
    }

    private async Task<List<ChatGptDecision>?> HandleMismatchSentences(List<Sentence>? sentences, IReadOnlyList<GeminiSentenceTag>? geminiTags, IReadOnlyList<PerplexitySentenceTag>? groqTags, List<ChatGptDecision>? chatGptDecisions, int batchSize, int i)
    {
        // Build compact arbitration prompt
        var promptBuilder = new System.Text.StringBuilder();
        //promptBuilder.AppendLine($"Use this document to generate the required responses. Document : {businessPromt}");
        promptBuilder.AppendLine("You are an arbiter. For each sentence provide a final informative type, confidence (0-1) and optional reason (JSON array).");
        promptBuilder.AppendLine("Sentences and tags:");
        foreach (var s in sentences.Skip(i).Take(batchSize))
        {
            var gq = groqTags.SingleOrDefault(x => x.SentenceId == s.Id);
            var gm = geminiTags.SingleOrDefault(x => x.SentenceId == s.Id);

            promptBuilder.AppendLine($"ID: {s.Id}");
            promptBuilder.AppendLine($"Text: {s.Text}");
            promptBuilder.AppendLine($"Groq: {JsonSerializer.Serialize(gq)}");
            promptBuilder.AppendLine($"Gemini: {JsonSerializer.Serialize(gm)}");
            promptBuilder.AppendLine();
        }

        var prompt = promptBuilder.ToString();
        prompt += $" Dont invent any new informativeType.... i am providing you the list of values.... anything else will be Uncertain. even with such information you have already provided wrong values...Return a JSON array where each element is an object with properties: " +
                        "\"SentenceId\" (string),\"\"Confidence\" (double),\"Reason\" (string), \"InformativeType\" (one of Fact|Claim|Definition|Opinion|Prediction|Statistic|Observation|Suggestion|Question|Transition|Filler|Uncertain), " +
                        "\"ClaimsCitation\" (boolean).If a sentence does not clearly fit a category, you MUST use 'Uncertain'. Do not invent new types. ONLY return the JSON array in the assistant response. The InformativeType must be one of the given values , if its not any of them then it should be Uncertain.Why the hell did you add a wrong value in InformativeType..... never ever ever add any value except from the list. Reason is string not json array. The response choices[0].message.Content is not coming correctly it should be proper json";

        string aiRaw = string.Empty;
        var cacheKey = _cache.ComputeRequestKey(prompt, "ChatGptArbitration");
        var cached = await _cache.GetAsync(cacheKey);
        if (cached != null)
            aiRaw = cached;
        else
        {
            aiRaw = await _openAi.CompleteAsync(prompt);


        }

        try
        {
            if (chatGptDecisions != null && chatGptDecisions.Any())
            {
                var res = JsonSerializer.Deserialize<ChatGptResponse>(aiRaw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (res != null)
                {
                    var content = res.Choices?.FirstOrDefault()?.Message?.Content;
                    chatGptDecisions.AddRange(JsonSerializer.Deserialize<List<ChatGptDecision>>(content));
                    if (chatGptDecisions != null)
                    {
                        await _cache.SaveAsync(cacheKey, aiRaw);
                    }
                }


            }
            else
            {
                var res = JsonSerializer.Deserialize<ChatGptResponse>(aiRaw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var content = res.Choices?.FirstOrDefault()?.Message?.Content;
                chatGptDecisions = JsonSerializer.Deserialize<List<ChatGptDecision>>(content);
                if (chatGptDecisions != null)
                {
                    await _cache.SaveAsync(cacheKey, aiRaw);
                }
            }
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync($"Error occured in handling mismatch : {ex.Message}:{ex.StackTrace}");
            // Fallback: prefer Gemini when AI response not parseable
            //chatGptDecisions = sentences.Select(s => new ChatGptDecision
            //{
            //    SentenceId = s.Id,
            //    FinalType = geminiTags.SingleOrDefault(g => g.SentenceId == s.Id)?.InformativeType ?? groqTags.Single(p => p.SentenceId == s.Id).InformativeType,
            //    Confidence = 0.9,
            //    Reason = "fallback to gemini due to unparsable AI response"
            //}).ToList();
        }

        return chatGptDecisions;
    }

    public async Task<double> GetKeywordScore(List<ValidatedSentence> validated, SeoRequest request)
    {
        var response = await GetSectionScoreResAsync(request.PrimaryKeyword);
        var h1 = validated.FirstOrDefault(vs => vs.HtmlTag.ToLower() == "h1")?.Text ?? string.Empty;
        var h2 = validated.FirstOrDefault(vs => vs.HtmlTag.ToLower() == "h2")?.Text ?? string.Empty;
        var h3 = validated.FirstOrDefault(vs => vs.HtmlTag.ToLower() == "h3")?.Text ?? string.Empty;
        var body = validated.FirstOrDefault(vs => vs.HtmlTag.ToLower() == "body")?.Text ?? string.Empty;
        return await KeywordScorer.CalculateKeywordScore(request.PrimaryKeyword, request.SecondaryKeywords, response.Variants, new ContentData()
        {
            H1 = h1,
            MetaDescription = request.MetaDescription,
            MetaTitle = request.MetaTitle,
            UrlSlug = request.Url,
            HeadersH2H3 = new List<string>() { h2, h3 },
            RawBodyText = body
        });
    }

    public async Task<double> GetIntentScoreInfo(string keyword)
    {
        var response = await GetSectionScoreResAsync(keyword);
        int match = 0;
        response.Competitors?.ToList()?.ForEach(c =>
        {
            if (c.Intent == response.Intent)
                match += 1;
        });
        //var intentScore = ((double)match / 5.0)*10;
        // return SectionScorer.Calculate(response?.Competitors, myHeadings);
        return match;
    }
    private async Task<SectionScoreResponse> GetSectionScoreResAsync(string keyword)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        var cacheKey = _cache.ComputeRequestKey(keyword, "SectionScores");
        var res = await _gemini.GetSectionScore(keyword);
        var cached = await _cache.GetAsync(cacheKey);
        SectionScoreResponse response = null;
        if (cached != null)
        {
            response = JsonSerializer.Deserialize<SectionScoreResponse>(cached, options);
            return response;
        }
        if (!string.IsNullOrEmpty(res))
        {
            var sectionScores = JsonSerializer.Deserialize<SectionScoreResponse>(res, options);
            if (sectionScores != null)
            {
                await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(sectionScores));
                response = sectionScores;

            }
        }
        return response;
    }
    public async Task<double> GetSectionScoreInfo(string keyword, List<ValidatedSentence> validatedSentences)
    {
        var response = await GetSectionScoreResAsync(keyword);
        List<string> myHeadings = validatedSentences.Where(vs => vs.HtmlTag.ToLower() == "h2" || vs.HtmlTag.ToLower() == "h3" || vs.HtmlTag.ToLower() == "h4").Select(x => x.Text).ToList();

        return SectionScorer.Calculate(response?.Competitors, myHeadings);
        //return 0.0;
    }

    public async Task<RecommendationResponse> GetRecommendationResponseAsync(string article)
    {
        var options = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };
        var cacheKey = _cache.ComputeRequestKey(article, "GeminiRecommendations:Complete");
        var cached = await _cache.GetAsync(cacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            return JsonSerializer.Deserialize<RecommendationResponse>(cached, options);
        }
        return new RecommendationResponse()
        {
            Recommendations = new List<Recommendation>(),
            Status = "NotStarted"
        };
    }

    public async Task<List<Recommendation>> GetFullRecommendationsAsync(string article, List<Level1Sentence> level1)
    {
        int offset = 100;
        var response = new List<Recommendation>();
        var options = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };
        var cacheKey = _cache.ComputeRequestKey(article, "GeminiRecommendations:Complete");
        var cached = await _cache.GetAsync(cacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            var recommendationRes=  JsonSerializer.Deserialize<RecommendationResponse>(cached, options);
            return recommendationRes?.Recommendations;
        }

        try
        {
            for (int i = 0; i < level1.Count; i += offset)
            {
                var chunk = level1.Skip(i).Take(offset).ToList();
                var level1Sentences = string.Join(" ", chunk.Select(s => s.Text));
                response.AddRange(await GenerateRecommendationsAsync(level1Sentences));
                if (response != null && response.Count > 0)
                    await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(new RecommendationResponse()
                    {
                        Recommendations = response,
                        Status = "InProgress"
                    }));
            }
            if (response != null && response.Count > 0)
                await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(new RecommendationResponse()
                {
                    Recommendations = response,
                    Status = "Complete"
                }));

        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync($"Error occured in getting full recommendation : {ex.Message}:{ex.StackTrace}");
            if (response != null && response.Count > 0)
                await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(new RecommendationResponse()
                {
                    Recommendations = response,
                    Status = "Error"
                }));
        }
        
        return response;
    }

    public async Task<List<Recommendation>> GenerateRecommendationsAsync(string article)
    {
        try
        {
            var options = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };
            var cacheKey = _cache.ComputeRequestKey(article, "GeminiRecommendations");
            var cached = await _cache.GetAsync(cacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                return JsonSerializer.Deserialize<List<Recommendation>>(cached, options); ;
            }


            var cachedRecommendations = await _cache.GetAsync(cacheKey);
            if(cachedRecommendations != null)
            {
                return JsonSerializer.Deserialize<List<Recommendation>>(cachedRecommendations, options);
            }

            var response = await _gemini.GenerateRecommendationsAsync(article);
            var recommendations = JsonSerializer.Deserialize<List<Recommendation>>(response, options);

            if (recommendations != null && recommendations.Count > 0)
                await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(recommendations));


            return recommendations;
        }
        catch(Exception ex)
        {
            await _logger.LogErrorAsync($"Error occured in generate recommendaton :  {ex.Message}:{ex.StackTrace}");
            throw;
        }
        

        return  new List<Recommendation>();
    }
}