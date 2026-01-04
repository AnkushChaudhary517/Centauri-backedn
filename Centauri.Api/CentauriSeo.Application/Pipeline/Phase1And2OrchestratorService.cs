using Azure;
using CentauriSeo.Application.Pipeline;
using CentauriSeo.Application.Services;
using CentauriSeo.Core.Models.Input;
using CentauriSeo.Core.Models.Output;
using CentauriSeo.Core.Models.Scoring;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Infrastructure.LlmClients;
using CentauriSeo.Infrastructure.LlmDtos;
using CentauriSeo.Infrastructure.Services;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace CentauriSeo.Application.Pipeline;

public class Phase1And2OrchestratorService
{
    private readonly GroqClient _groq;
    private readonly GeminiClient _gemini;
    private readonly OpenAiClient _openAi; // used for ChatGPT arbitration
    private readonly ILlmCacheService _cache;

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
    }

    // Runs Groq + Gemini tagging, detects mismatches, asks OpenAI (ChatGPT) for arbitration when needed,
    // then returns the validated sentence map using the existing Phase2_ArbitrationEngine.Execute flow.
    public async Task<IReadOnlyList<ValidatedSentence>> RunAsync(ArticleInput article, string businessPromt)
    {
        var sentences = Phase0_InputParser.Parse(article);

        // 1) Fetch tags separately: use Groq instead of Perplexity for Level-1 tagging
        var groqTags = (await _groq.TagSentencesAsync(sentences, businessPromt)).ToList();
        var geminiTags = (await _gemini.TagSentencesAsync(sentences,businessPromt)).ToList();

        // 2) Detect mismatches (informative type OR citation flag OR voice)
        var anyMismatch = sentences.Where(s =>
        {
            var gq = groqTags.SingleOrDefault(x => x.SentenceId == s.Id);
            var gm = geminiTags.SingleOrDefault(x => x.SentenceId == s.Id);
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
            promptBuilder.AppendLine($"Use this document to generate the required responses. Document : {businessPromt}");
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
                    await _cache.SaveAsync(cacheKey, aiRaw);
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
                    }
                }
                catch
                {
                    // Fallback: prefer Gemini when AI response not parseable
                    chatGptDecisions = sentences.Select(s => new ChatGptDecision
                    {
                        SentenceId = s.Id,
                        FinalType = geminiTags.SingleOrDefault(g => g.SentenceId == s.Id)?.InformativeType ?? groqTags.Single(p => p.SentenceId == s.Id).InformativeType,
                        Confidence = 0.9,
                        Reason = "fallback to gemini due to unparsable AI response"
                    }).ToList();
                }
            }

            
        }

        // 3) Run arbitration engine to produce validated sentences TODO: pass Groq tags when implemented
        var validated = new Phase1And2Orchestrator().Execute(sentences, groqTags, geminiTags, chatGptDecisions);
        return validated;
    }


    public async Task<List<Recommendation>> GenerateRecommendationsAsync(Level2Scores l2, Level3Scores l3, Level4Scores l4)
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

        // 2️⃣ System prompt (instructions for AI)
        string systemPrompt = @"
You are an SEO content optimization expert.
Generate actionable content improvement recommendations based on the provided scores.

Rules:
- Only generate recommendations for scores below acceptable thresholds.
- Output VALID JSON ONLY.
- Do NOT include markdown.
- Do NOT include explanations.
- Use this exact schema:

[
  {
    ""issue"": string,
    ""whatToChange"": string,
    ""examples"": {
      ""bad"": string,
      ""good"": string
    },
    ""improves"": string[]
  }
]

Scoring thresholds:
- Simplicity < 2
- Grammar < 2
- Keyword < 5
- Readability < 7
- EEAT < 18
- AI Indexing < 50
- Overall SEO < 40
";

        // 3️⃣ User prompt (JSON of the scores)
        string userPrompt = JsonSerializer.Serialize(issues);
        try
        {
            var cacheKey = _cache.ComputeRequestKey(userPrompt + systemPrompt, "GeminiRecommendations");
            var cached = await _cache.GetAsync(cacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                return JsonSerializer.Deserialize<List<Recommendation>>(cached, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }); ;
            }
            var response = await _gemini.GenerateAsync(userPrompt, systemPrompt);
            var res = JsonSerializer.Deserialize<GeminiApiResponse>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            string aiContent = res?.Candidates?.ToList()?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;


            // 5️⃣ Deserialize JSON safely
            var recommendations = JsonSerializer.Deserialize<List<Recommendation>>(aiContent,new JsonSerializerOptions() { PropertyNameCaseInsensitive=true});
            
            if(recommendations != null && recommendations.Count>0)
                await _cache.SaveAsync(cacheKey, aiContent);


            return recommendations;
        }
        catch(Exception ex)
        {
        }
        

        return  new List<Recommendation>();
    }
}