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

    public async Task<(IReadOnlyList<GeminiSentenceTag>, IReadOnlyList<PerplexitySentenceTag>)> ParallelTaggingAsync(List<Sentence> sentences)
    {
        // 1. Prepare chunks
        int chunkSize = 150;
        var chunks = sentences.Chunk(chunkSize).ToList();

        // 2. Initialize Task lists for both providers
        var geminiTasks = new List<Task<IReadOnlyList<GeminiSentenceTag>>>();
        var groqTasks = new List<Task<IReadOnlyList<PerplexitySentenceTag>>>();

        // 3. Fire all requests immediately without awaiting
        foreach (var chunk in chunks)
        {
            var minifiedJson = JsonSerializer.Serialize(chunk);

            // Start Gemini task and add to list
            geminiTasks.Add(_gemini.TagArticleAsync(
                SentenceTaggingPrompts.GeminiSentenceTagPrompt,
                minifiedJson,
                "gemini:tagging"
            ));

            // Start Groq task and add to list
            groqTasks.Add(_groq.TagSentencesAsync(minifiedJson, string.Empty));
        }
        var startTime = DateTime.UtcNow;
        // 4. Await everything at once (Total parallelism)
        await Task.WhenAll(geminiTasks.Cast<Task>().Concat(groqTasks));
        var endTime = DateTime.UtcNow;
        // 5. Aggregate the results using SelectMany
        var allGeminiResults = geminiTasks.SelectMany(t => t.Result).ToList();
        var allGroqResults = groqTasks.SelectMany(t => t.Result).ToList();
        return (allGeminiResults, allGroqResults);
    }


    public async Task<List<GeminiSentenceTag>> GetSentenceTaggingFromLocalLLP(string primaryKeyword, List<Sentence> sentences)
    {
         HttpClient client = new HttpClient();
        string apiUrl = "http://ec2-15-206-164-71.ap-south-1.compute.amazonaws.com:8000/analyze";
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };
            var inputData = JsonSerializer.Serialize(new { 
            sentences=sentences,
            primaryKeyword= primaryKeyword
            });
            Console.WriteLine("Sending request to Centauri NLP Service...");
            var content = new StringContent(inputData, System.Text.Encoding.UTF8, "application/json");
            // 3. Make the POST request
            var response = await client.PostAsync(apiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                // 4. Deserialize the response
                var res = await response.Content.ReadAsStringAsync();
                var results = JsonSerializer.Deserialize<List<GeminiSentenceTag>>(res, options);
                return results;
            }
            else
            {
            }
        }
        catch (Exception ex)
        {
        }
        return null;
    }
    // Runs Groq + Gemini tagging, detects mismatches, asks OpenAI (ChatGPT) for arbitration when needed,
    // then returns the validated sentence map using the existing Phase2_ArbitrationEngine.Execute flow.
    public async Task<OrchestratorResponse> RunAsync(SeoRequest request)
    {
        //var sentences1 = Phase0_InputParser.TagArticleLikeGemini(request.Article.Raw);
        //var sentences2 = Phase0_InputParser.TagByHtmlStructure(request.Article.Raw);
        var sentenceTagging = Phase0_InputParser.TagArticleProfessional(request.Article.Raw);
        //var sentenceTagging = (await _gemini.TagArticleAsync(SentenceTaggingPrompts.SentenceTaggingPrompt, request.Article.Raw, "gemini:tagging:level1")).ToList();
        var sentences = sentenceTagging?.Select(s => new Sentence(s.SentenceId, s.Sentence,0) ).ToList();
        //var userContent = JsonSerializer.Serialize(sentences);


        //var geminiTask = _gemini.TagArticleAsync(SentenceTaggingPrompts.GeminiSentenceTagPrompt, userContent, "gemini:tagging");
        //var groqTask = _groq.TagSentencesAsync(userContent,string.Empty);       
        //await Task.WhenAll(geminiTask, groqTask);

        var localLlpTags = await GetSentenceTaggingFromLocalLLP(request.PrimaryKeyword, sentences);
        var geminiTags = await _gemini.TagArticleAsync(
                SentenceTaggingPrompts.GeminiSentenceTagPrompt,
                JsonSerializer.Serialize(sentences),
                "gemini:tagging"
            );

        //(var geminiTags, var groqTags) = await ParallelTaggingAsync(sentences);
        ////var geminiTags = await geminiTask;
        geminiTags?.ToList()?.ForEach(g =>
        {
            var sentence = sentenceTagging.FirstOrDefault(s => s.SentenceId == g.SentenceId);
            g.Sentence = sentence?.Sentence ?? string.Empty;
            g.HtmlTag = sentence?.HtmlTag ?? string.Empty;
            g.ParagraphId = sentence?.ParagraphId ?? string.Empty;
        });

        ////var groqTags = await groqTask;
        //localLlpTags?.ToList()?.ForEach(g =>
        //{
        //    var sentence = sentenceTagging.FirstOrDefault(s => s.SentenceId == g.SentenceId);
        //    g.Sentence = sentence?.Sentence ?? string.Empty;
        //    g.HtmlTag = sentence?.HtmlTag ?? string.Empty;
        //});
        // 2) Detect mismatches (informative type OR citation flag OR voice)
        var anyMismatch = geminiTags?.Where(gm =>
        {
            var gq = localLlpTags.FirstOrDefault(x => x.Sentence == gm.Sentence);


            //var gm = geminiTags.SingleOrDefault(x => x.SentenceId == s.Id);
            if (gq == null || gm == null) return true;
            gm.Structure = gq.Structure;
            gm.Voice = gq.Voice;
            gm.ClaimsCitation = gq.ClaimsCitation;
            gm.IsGrammaticallyCorrect = gq.IsGrammaticallyCorrect;
            gm.HasPronoun = gq.HasPronoun;
            gm.FunctionalType = gq.FunctionalType;
            gm.InfoQuality = gq.InfoQuality;
            if (gq.InformativeType != gm.InformativeType)
            {
                return true;
            }
            //if (gq.ClaimsCitation != gm.ClaimsCitation) return true;
            //if (gq.FunctionalType != gm.FunctionalType) return true;
            //if (gq.Voice != gm.Voice) return true;
            // voice/structure mismatches handled by Gemini mostly; ignore deterministic detector differences
            return false;
        }).ToList();

        List<ChatgptGeminiSentenceTag>? chatGptDecisions = new List<ChatgptGeminiSentenceTag>();
       
        if (anyMismatch != null && anyMismatch.Count>0)
        {

            var tasks = new List<Task<List<ChatgptGeminiSentenceTag>>>();
            var batchSize = 100;
            for (int i=0;i< anyMismatch.Count; i+=batchSize)
            {
                tasks.Add(HandleMismatchSentences(anyMismatch.Skip(i).Take(batchSize).ToList(), geminiTags, localLlpTags, chatGptDecisions));               
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

        var validated = new Phase1And2Orchestrator().Execute(sentences, localLlpTags, geminiTags, chatGptDecisions);
        return new OrchestratorResponse()
        {
            ValidatedSentences = validated,
            PlagiarismScore = await _gemini.GetPlagiarismScore(sentences),
            SectionScore = await GetSectionScoreInfo(request.PrimaryKeyword, validated?.ToList()),
            IntentScore = await GetIntentScoreInfo(request.PrimaryKeyword),
            KeywordScore = await GetKeywordScore(validated?.ToList(), request),
            AnswerPositionIndex = await GetAnswerPositionIndex(validated?.ToList(), request),
            Sections = BuildSections(validated?.ToList())
        };
    }

    public static List<Section> BuildSections(List<ValidatedSentence> sentences)
    {
        var sections = new List<Section>();
        Section currentSection = null;
        int sectionCounter = 1;

        bool IsHeader(string tag) =>
            tag.Equals("h2", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("h3", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("h4", StringComparison.OrdinalIgnoreCase);

        foreach (var sentence in sentences)
        {
            if (IsHeader(sentence.HtmlTag))
            {
                // Close previous section (if any)
                if (currentSection != null)
                {
                    sections.Add(currentSection);
                }

                // Start new section
                currentSection = new Section
                {
                    Id = $"S{sectionCounter++}",
                    SectionText = sentence.Text,
                    SentenceIds = new List<string>()
                };
            }
            else
            {
                // Content sentence → attach to current section
                if (currentSection != null)
                {
                    currentSection.SentenceIds.Add(sentence.Id);
                }
                // else: content before first header → ignored by definition
            }
        }

        // Add last section
        if (currentSection != null)
        {
            sections.Add(currentSection);
        }

        return sections;
    }
    public List<Level1Sentence> FilterIrrelevantSentences(List<ValidatedSentence> localAnalysis, string primaryKeyword)
    {
        var relevantList = new List<Level1Sentence>();
        string keywordLower = primaryKeyword.ToLower();

        foreach (var s in localAnalysis)
        {
            bool hasDirectKeyword = s.Text.ToLower().Contains(keywordLower);

            // 1. Agar RelevanceScore low hai aur Direct Keyword bhi nahi hai -> Drop it
            // Threshold 0.5 typical standard hai 'lg' model ke liye
            if (s.RelevanceScore < 0.5 && !hasDirectKeyword)
            {
                continue;
            }

            // 2. Rule 1 logic: Meaning complete without pronouns
            // Agar keyword nahi hai aur sentence pronoun se bhara hai, toh relevance bhale hi thodi ho, filter kar do
            //if (!hasDirectKeyword && s.HasPronoun && s.RelevanceScore < 0.7)
            //{
            //    continue;
            //}

            // 3. Junk filtering (Optional but good)
            if (s.InformativeType == Core.Models.Enums.InformativeType.Filler) continue;

            relevantList.Add(new Level1Sentence { Id = s.Id, Text = s.Text });
        }

        return relevantList;
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

            var aiIndexingResponse = new AiIndexinglevel1Response();
            var resList = await _gemini.GetLevel1InforForAIIndexing(request.PrimaryKeyword, FilterIrrelevantSentences(validatedSentences,request.PrimaryKeyword), 100);
            resList?.ForEach(res =>
            {
                try
                {
                    var deserialized = JsonSerializer.Deserialize<AiIndexinglevel1Response>(res, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (deserialized?.Sentences != null && deserialized.Sentences.Count > 0)
                    {
                        aiIndexingResponse.Sentences.AddRange(deserialized.Sentences);
                    }

                }
                catch(Exception ex)
                {
                    _logger.LogErrorAsync($"Error occured in parsing ai indexing response : {ex.Message}:{ex.StackTrace}").Wait();
                }

            });


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
                        vs.EntityMentionFlag = s.EntityMentionFlag ?? new EntityMentionFlag() { Entities = null, EntityCount = 0, Value = 0 }; ;
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

            var firstAnswerSentenceId = aiIndexingResponse.AnswerPositionIndex.FirstAnswerSentenceId;
            var finalAnswer = 0.0;
            var pScore = validatedSentences.IndexOf(validatedSentences.Where(x => x.Id == firstAnswerSentenceId).FirstOrDefault());
            var percent = ((double)pScore / validatedSentences.Count) * 100;
            switch (percent)
            {
                case <= 5:
                    finalAnswer = 1;
                    break;
                case <= 10:
                    finalAnswer = 0.75;
                    break;
                case <= 20:
                    finalAnswer = 0.5;
                    break;
                case <= 30:
                    finalAnswer = .25;
                    break;
                default:
                    finalAnswer = 0.0;
                    break;
            }
            return new AnswerPositionIndex() { FirstAnswerSentenceId = firstAnswerSentenceId, PositionScore = finalAnswer };
        }
        catch(Exception ex)
        {
            await _logger.LogErrorAsync($"Error occured in getting answer position index : {ex.Message}:{ex.StackTrace}");
        }

        return new AnswerPositionIndex() {FirstAnswerSentenceId = null, PositionScore = 0.0 };
    }

    private async Task<List<ChatgptGeminiSentenceTag>?> HandleMismatchSentences(List<GeminiSentenceTag>? mismatchedSentences, IReadOnlyList<GeminiSentenceTag>? geminiTags, IReadOnlyList<GeminiSentenceTag>? localTags, List<ChatgptGeminiSentenceTag>? chatGptDecisions)
    {

        var mismatchSentences = mismatchedSentences.Select(x => new { Sentenceid=x.SentenceId,Sentence= x.Sentence }).ToList();

       
        string aiRaw = string.Empty;
        var prompt = JsonSerializer.Serialize(new
        {
            SystemRequirement = SentenceTaggingPrompts.ChatGptTagPromptConcise,
            UserContent = mismatchSentences
        });
        var cacheKey = _cache.ComputeRequestKey(prompt, "Chatgpt:Arbitration");
        var cached = await _cache.GetAsync(cacheKey);
        var done = false;
        var excetion = string.Empty;
        var retryCount = 0;
        while (!done && retryCount<2)
        {
            if (cached != null)
            {
                aiRaw = cached;

            }
            else
            {
                if(!string.IsNullOrEmpty(excetion))
                {
                    prompt += $"Exception : this error occred in previous call. Do not repeat the error again and fix the response. : {excetion}.";
                }
                aiRaw = await _openAi.CompleteAsync(prompt);


            }


            try
            {
                var options = new JsonSerializerOptions
                {
                    Converters =
                    {
                            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) // or null for exact match,
                    },
                    PropertyNameCaseInsensitive = true
                };
                if (chatGptDecisions != null && chatGptDecisions.Any())
                {
                    var res = JsonSerializer.Deserialize<ChatGptResponse>(aiRaw, options);
                    if (res != null)
                    {
                        var content = res.Choices?.FirstOrDefault()?.Message?.Content;
                        chatGptDecisions.AddRange(JsonSerializer.Deserialize<List<ChatgptGeminiSentenceTag>>(content));
                        if (chatGptDecisions != null)
                        {
                            await _cache.SaveAsync(cacheKey, aiRaw);
                        }
                    }


                }
                else
                {
                    var res = JsonSerializer.Deserialize<ChatGptResponse>(aiRaw, options);
                    var content = res.Choices?.FirstOrDefault()?.Message?.Content;
                    chatGptDecisions = JsonSerializer.Deserialize<List<ChatgptGeminiSentenceTag>>(content, options);
                    if (chatGptDecisions != null)
                    {
                        await _cache.SaveAsync(cacheKey, aiRaw);
                    }
                }
                done = true;
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync($"Error occured in handling mismatch : {ex.Message}:{ex.StackTrace}");
                excetion = ex.Message;
                retryCount++;
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
        

        return chatGptDecisions;
    }

    public async Task<double> GetKeywordScore(List<ValidatedSentence> validated, SeoRequest request)
    {
        var response = await GetSectionScoreResAsync(request.PrimaryKeyword);
        var h1 = validated.FirstOrDefault(vs => vs.HtmlTag.ToLower() == "h1")?.Text ?? string.Empty;
        var h2 = validated.FirstOrDefault(vs => vs.HtmlTag.ToLower() == "h2")?.Text ?? string.Empty;
        var h3 = validated.FirstOrDefault(vs => vs.HtmlTag.ToLower() == "h3")?.Text ?? string.Empty;
        var body = string.Join("",validated.Where(vs => vs.HtmlTag.ToLower() != "h1" && vs.HtmlTag.ToLower() != "h2" && vs.HtmlTag.ToLower() != "h3")?.Select(x => x.Text).ToList()) ?? string.Empty;
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
        var response = new SectionScoreResponse();
        var retryCount = 0;
        var done = false;
        var exception = string.Empty;
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };
            var cacheKey = _cache.ComputeRequestKey(keyword, "SectionScores");
            var cached = await _cache.GetAsync(cacheKey);
            if (cached != null)
            {
                response = JsonSerializer.Deserialize<SectionScoreResponse>(cached, options);
                return response;
            }
            while (!done && retryCount < 2)
            {
                try
                {
                    var res = await _gemini.GetSectionScore(keyword, exception);
                    if (!string.IsNullOrEmpty(res))
                    {
                        var sectionScores = JsonSerializer.Deserialize<SectionScoreResponse>(res, options);
                        if (sectionScores != null)
                        {
                            await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(sectionScores));
                            response = sectionScores;

                        }
                    }
                    done = true;

                }
                catch(Exception ex)
                {
                    await _logger.LogErrorAsync($"Error occured in getting section score response : {ex.Message}:{ex.StackTrace}");
                    retryCount++;
                }
               
            }
            
        }
        catch(Exception ex)
        {
            
            await _logger.LogErrorAsync($"Error occured in getting section score response : {ex.Message}:{ex.StackTrace}");
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

    public async Task<RecommendationResponseDTO> GetFullRecommendationsAsync(string article, List<ValidatedSentence> level1, List<Section> sections)
    {
        int offset = 100;
        var response = new RecommendationResponseDTO()
        {
            Recommendations = new RecommendationsResponse(),
            Status = "InProgress"
        };
        var options = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };
        var cacheKey = _cache.ComputeRequestKey(article, "GeminiRecommendations:Complete");
        var cached = await _cache.GetAsync(cacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            var recommendationRes=  JsonSerializer.Deserialize<RecommendationResponseDTO>(cached, options);
            return recommendationRes;
        }

        try
        {
            List<Task<RecommendationsResponse>> tasks = new List<Task<RecommendationsResponse>>();
            var request = JsonSerializer.Serialize(new
            {
                Sections=sections,
                Sentences = level1.Select(x => new
                {
                    Id = x.Id,
                    Text = x.Text,
                    HtmlTag = x.HtmlTag
                }).ToList()
            });
            response.Recommendations = await GenerateRecommendationsAsync(JsonSerializer.Serialize(request));
            
           // for (int i = 0; i < level1.Count; i += offset)
           // {
           //     var chunk = level1.Skip(i).Take(offset).ToList();
           //     var level1Sentences = string.Join(" ", chunk.Select(s => new { Text = s.Text, HtmlTag = s.HtmlTag }));
           //     tasks.Add(GenerateRecommendationsAsync(level1Sentences));

            // }
            //var res = await Task.WhenAll(tasks);
            //res?.ToList()?.ForEach(r =>
            //{
            //    response.Recommendations.Add(r);
            //});

            if (response?.Recommendations?.Overall != null && response.Recommendations.Overall.Count > 0)
            {
                response.Status = "Completed";
                await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(response));
            }
              

        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync($"Error occured in getting full recommendation : {ex.Message}:{ex.StackTrace}");
            if (response?.Recommendations?.Overall != null && response.Recommendations.Overall.Count > 0)
            {
                response.Status = "Error";
                await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(response));
            }
                
        }
        
        return response;
    }

    private object GetSection(List<ValidatedSentence> level1, ValidatedSentence validatedSentence)
    {
        return null;
    }

    public async Task<RecommendationsResponse> GenerateRecommendationsAsync(string article)
    {
        try
        {
            var options = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true,
                Converters =
                    {
                            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) // or null for exact match,
                    },
            };
            var cacheKey = _cache.ComputeRequestKey(article, "GeminiRecommendations");
            var cached = await _cache.GetAsync(cacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                return JsonSerializer.Deserialize<RecommendationsResponse>(cached, options); ;
            }


            var cachedRecommendations = await _cache.GetAsync(cacheKey);
            if(cachedRecommendations != null)
            {
                return JsonSerializer.Deserialize<RecommendationsResponse>(cachedRecommendations, options);
            }

            var response = await _gemini.GenerateRecommendationsAsync(article);
            var recommendations = JsonSerializer.Deserialize<RecommendationsResponse>(response, options);

            if (recommendations != null)
                await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(recommendations));


            return recommendations;
        }
        catch(Exception ex)
        {
            await _logger.LogErrorAsync($"Error occured in generate recommendaton :  {ex.Message}:{ex.StackTrace}");
            throw;
        }
        

        return  new RecommendationsResponse();
    }
}