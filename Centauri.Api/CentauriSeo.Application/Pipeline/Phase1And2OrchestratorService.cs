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
        // 1) Fetch tags separately: use Groq instead of Perplexity for Level-1 tagging
        var geminiTags = (await _gemini.TagArticleAsync(SentenceTaggingPrompts.GeminiSentenceTagPrompt, userContent, "gemini:tagging")).ToList();
        geminiTags?.ForEach(g =>
        {
            var sentence = sentenceTagging.FirstOrDefault(s => s.SentenceId == g.SentenceId);
            g.Sentence = sentence?.Sentence ?? string.Empty;
            g.HtmlTag = sentence?.HtmlTag ?? string.Empty;
        });

        var groqTags = (await _groq.TagSentencesAsync(userContent,string.Empty)).ToList();
        groqTags?.ForEach(g =>
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

        List<ChatGptDecision>? chatGptDecisions = null;

        if (anyMismatch != null && anyMismatch.Count>0)
        {
            // Build compact arbitration prompt
            var promptBuilder = new System.Text.StringBuilder();
            //promptBuilder.AppendLine($"Use this document to generate the required responses. Document : {businessPromt}");
            promptBuilder.AppendLine("You are an arbiter. For each sentence provide a final informative type, confidence (0-1) and optional reason (JSON array).");
            promptBuilder.AppendLine("Sentences and tags:");

            var batchSize = 100;
            for (int i=0;i< anyMismatch.Count; i+=batchSize)
            {
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
                if(cached != null)
                    aiRaw = cached;
                else
                {
                    aiRaw = await _openAi.CompleteAsync(prompt);
                    
                    
                }

                try
                {
                    if(chatGptDecisions != null && chatGptDecisions.Any())
                    {
                        var res = JsonSerializer.Deserialize<ChatGptResponse>(aiRaw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if(res != null)
                        {
                            var content = res.Choices?.FirstOrDefault()?.Message?.Content;
                            chatGptDecisions.AddRange(JsonSerializer.Deserialize<List<ChatGptDecision>>(content));
                        }
                           

                    }
                    else
                    {
                        var res = JsonSerializer.Deserialize<ChatGptResponse>(aiRaw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        var content = res.Choices?.FirstOrDefault()?.Message?.Content;
                        chatGptDecisions = JsonSerializer.Deserialize<List<ChatGptDecision>>(content);
                        if(chatGptDecisions != null)
                        {
                            await _cache.SaveAsync(cacheKey, aiRaw);
                        }
                    }
                }
                catch(Exception ex)
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
            }

            
        }

        // 3) Run arbitration engine to produce validated sentences TODO: pass Groq tags when implemented
        var validated = new Phase1And2Orchestrator().Execute(sentences, groqTags, geminiTags, chatGptDecisions);
        return new OrchestratorResponse()
        {
            ValidatedSentences = validated,
            PlagiarismScore = await _gemini.GetPlagiarismScore(sentences),
            SectionScore = await GetSectionScoreInfo(request.PrimaryKeyword, validated?.ToList()),
            IntentScore = await GetIntentScoreInfo(request.PrimaryKeyword),
            KeywordScore = await GetKeywordScore(validated?.ToList(), request)
        };
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

    public async Task<List<Recommendation>> GenerateRecommendationsAsync(string article,Level2Scores l2, Level3Scores l3, Level4Scores l4)
    {
        try
        {
            var issues = new
            {
                SimplicityScore = l2.SimplicityScore,
                GrammarScore = l2.GrammarScore,
                KeywordScore = l2.KeywordScore,
                ReadabilityScore = l3.ReadabilityScore,
                EeatScore = l3.EeatScore,
                AiIndexingScore = l4.AiIndexingScore,
                SeoScore = l4.CentauriSeoScore
            };

            var cacheKey = _cache.ComputeRequestKey(article, "GeminiRecommendations");
            var cached = await _cache.GetAsync(cacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                return JsonSerializer.Deserialize<List<Recommendation>>(cached, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }); ;
            }


            var cachedRecommendations = await _cache.GetAsync(cacheKey);
            if(cachedRecommendations != null)
            {
                return JsonSerializer.Deserialize<List<Recommendation>>(cachedRecommendations, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            var response = await _gemini.GenerateRecommendationsAsync(article);
            var recommendations = JsonSerializer.Deserialize<List<Recommendation>>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            //string aiContent = res?.Candidates?.ToList()?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;


            //// 5️⃣ Deserialize JSON safely
            //var recommendations = JsonSerializer.Deserialize<List<Recommendation>>(aiContent,new JsonSerializerOptions() { PropertyNameCaseInsensitive=true});

            if (recommendations != null && recommendations.Count > 0)
                await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(recommendations));


            return recommendations;
        }
        catch(Exception ex)
        {
            await _logger.LogErrorAsync($"Error occured in generate recommendaton :  {ex.Message}:{ex.StackTrace}");
        }
        

        return  new List<Recommendation>();
    }
}