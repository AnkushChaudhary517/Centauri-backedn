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

    public async Task<AiIndexinglevelLocalLlmResponse> GetSentenceTaggingFromLocalLLP(string primaryKeyword, List<Sentence> sentences)
    {
         HttpClient client = new HttpClient();
        string apiUrl = "http://ec2-15-206-164-71.ap-south-1.compute.amazonaws.com:8000/analyze";
        //string apiUrl = "http://localhost:8000/analyze";
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
                var results = JsonSerializer.Deserialize<AiIndexinglevelLocalLlmResponse>(res, options);
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
    public async Task<OrchestratorResponse> RunAsync(SeoRequest request, AiIndexinglevelLocalLlmResponse fullLocalLlmTags)
    {
        var sectionScore = await GetSectionScoreInfo(request.PrimaryKeyword,request.SecondaryKeywords, fullLocalLlmTags.Sentences);
        var intentScoreTask = GetIntentScoreInfo(request.PrimaryKeyword);
        var keywordScoreTask = GetKeywordScore(fullLocalLlmTags.Sentences, request);
        var sentences = fullLocalLlmTags.Sentences.Select(s => new Sentence(s.SentenceId, s.Sentence, s.ParagraphId)).ToList();

        //var serialized = "[{\"SentenceId\":\"S1\",\"Sentence\":\"What Are the IRS Requirements for LLC Dissolution Based on Tax Classification\",\"HtmlTag\":\"h1\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":2,\"FunctionalType\":1,\"InformativeType\":8,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S2\",\"Sentence\":\"Meta Title: How the IRS Handles LLC Dissolution and EIN Closure\",\"HtmlTag\":\"p\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S3\",\"Sentence\":\"Meta Description : Understand IRS LLC dissolution rules by tax type. Learn which IRS forms to file, how to close your EIN, and avoid penalties when shutting down your business.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":7,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S4\",\"Sentence\":\"URL Slug: irs-llc-dissolution\",\"HtmlTag\":\"p\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S5\",\"Sentence\":\"When you dissolve an LLC, the IRS does not care what your state calls the process. It only looks at how your LLC is taxed. Whether your LLC is treated as a sole proprietorship, partnership, or corporation determines which IRS forms you must file, which deadlines apply, and whether additional steps like Form 966 or EIN closure are required.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S6\",\"Sentence\":\"If you miss these steps, your LLC could remain \\u201Cactive\\u201D in the IRS system, even after state dissolution. That can trigger penalties, late notices, or an extra tax year you did not plan for. This guide explains how IRS LLC dissolution works based on tax classification, what forms are required in each case, and how to close your business cleanly without future IRS issues.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":2,\"FunctionalType\":0,\"InformativeType\":11,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S7\",\"Sentence\":\"What the IRS Requires for an LLC Dissolution\",\"HtmlTag\":\"h2\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":1,\"InformativeType\":0,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S8\",\"Sentence\":\"No matter how your LLC is taxed, the IRS expects a clean tax closure. This means filing final returns, closing employment and information filings, and formally notifying the IRS that the business will no longer operate.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S9\",\"Sentence\":\"Step / Requirement\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S10\",\"Sentence\":\"Sole Proprietorship (Single-Member LLC)\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":2,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S11\",\"Sentence\":\"Partnership (Multi-Member LLC)\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":2,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S12\",\"Sentence\":\"Corporation (C- or S-Corp LLC)\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":2,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S13\",\"Sentence\":\"Final income tax return\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S14\",\"Sentence\":\"Schedule C with Form 1040 marked \\u201CFinal return\\u201D\",\"HtmlTag\":\"td\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S15\",\"Sentence\":\"Form 1065 marked \\u201CFinal return\\u201D\",\"HtmlTag\":\"td\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S16\",\"Sentence\":\"Form 1120 or 1120-S marked \\u201CFinal return\\u201D\",\"HtmlTag\":\"td\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S17\",\"Sentence\":\"Owner / member reporting\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S18\",\"Sentence\":\"Schedule SE if net earnings \\u2265 $400\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S19\",\"Sentence\":\"Final Schedule K-1s issued to all members\",\"HtmlTag\":\"td\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S20\",\"Sentence\":\"Final K-1s (S-corp) or shareholder distribution reporting\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S21\",\"Sentence\":\"Asset sales or liquidation\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S22\",\"Sentence\":\"Form 4797; Form 8594 if assets sold\",\"HtmlTag\":\"td\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S23\",\"Sentence\":\"Form 4797; Form 8594; report liquidating distributions\",\"HtmlTag\":\"td\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S24\",\"Sentence\":\"Payroll \\u0026 information returns (if applicable)\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S25\",\"Sentence\":\"Forms 941/944, 940, W-2/W-3, 1099/1096\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S26\",\"Sentence\":\"Form 966 required\",\"HtmlTag\":\"td\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S27\",\"Sentence\":\"No\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S28\",\"Sentence\":\"Yes, within 30 days of dissolution resolution\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S29\",\"Sentence\":\"EIN closure\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S30\",\"Sentence\":\"Written request to IRS after final filings\",\"HtmlTag\":\"td\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S31\",\"Sentence\":\"IRS complexity level\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S32\",\"Sentence\":\"Low\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S33\",\"Sentence\":\"Medium\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S34\",\"Sentence\":\"High\",\"HtmlTag\":\"td\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S35\",\"Sentence\":\"Skipping any of these steps can keep your LLC open in IRS records. Here\\u2019s what you\\u2019re supposed to do:-\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":9,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S36\",\"Sentence\":\"i) Final Returns Must Be Filed Using the Right Form\",\"HtmlTag\":\"h3\",\"Structure\":2,\"Voice\":1,\"InfoQuality\":3,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S37\",\"Sentence\":\"When you dissolve an LLC, you must file a final federal tax return using the same form your business normally files. The key requirement is to check the box marked \\u201CFinal return.\\u201D This tells the IRS that the business has permanently stopped operating and should not be expected to file again.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S38\",\"Sentence\":\"The form you file depends on how your LLC is taxed. Single-member LLCs use Schedule C with Form 1040. Partnerships file Form 1065. LLCs taxed as corporations file Form 1120 or Form 1120-S. Filing the wrong form or forgetting to mark it as final is one of the most common IRS dissolution mistakes.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S39\",\"Sentence\":\"ii) Payroll, Information, and Excise Returns Must Be Closed\",\"HtmlTag\":\"h3\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S40\",\"Sentence\":\"If your LLC had employees, contractors, or excise tax obligations, those filings must also be closed. Even if the business stopped mid-year, the IRS still expects final versions of these returns.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S41\",\"Sentence\":\"You may need to file:\",\"HtmlTag\":\"p\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":11,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S42\",\"Sentence\":\"Forms 941 or 944 to report final employment taxes\",\"HtmlTag\":\"li\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S43\",\"Sentence\":\"Form 940 for final federal unemployment tax\",\"HtmlTag\":\"li\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S44\",\"Sentence\":\"Forms W-2 and W-3 for employees\",\"HtmlTag\":\"li\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S45\",\"Sentence\":\"Forms 1099 and 1096 for contractors\",\"HtmlTag\":\"li\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S46\",\"Sentence\":\"Excise tax returns , if your business was subject to them\",\"HtmlTag\":\"li\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S47\",\"Sentence\":\"Each of these filings must reflect that no future wages, payments, or taxable activity will occur.\",\"HtmlTag\":\"p\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S48\",\"Sentence\":\"iii) Close Your EIN With the IRS\",\"HtmlTag\":\"h3\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S49\",\"Sentence\":\"An EIN is never automatically closed when an LLC dissolves. After filing all final returns, you must notify the IRS in writing to close it. This is done by sending a letter to the IRS that includes the legal name of the LLC, the EIN, the business address, and the reason for closure.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":4,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S50\",\"Sentence\":\"Until this step is completed, the IRS may continue to expect filings, even if the business no longer exists at the state level.\",\"HtmlTag\":\"p\",\"Structure\":2,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":11,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":true,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S51\",\"Sentence\":\"How to Dissolve an LLC Taxed as a Sole Proprietorship\",\"HtmlTag\":\"h2\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":1,\"InformativeType\":0,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S52\",\"Sentence\":\"Single-member LLCs are treated as disregarded entities for federal tax purposes. This means the IRS looks directly at the owner\\u2019s individual return rather than a separate business return. The dissolution process is simpler than other tax classifications, but the final filings still need to be handled correctly.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S53\",\"Sentence\":\"When you dissolve a single-member LLC, you must file your individual tax return and include Schedule C for the business. On Schedule C, you must check the box marked \\u201CFinal return.\\u201D This tells the IRS that the business has permanently stopped operating and will not file again in future years.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S54\",\"Sentence\":\"In addition to the final Schedule C, you may need to complete the following depending on what happened during closure:\",\"HtmlTag\":\"p\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S55\",\"Sentence\":\"Form 4797 if the LLC sold or disposed of business property such as equipment or vehicles\",\"HtmlTag\":\"li\",\"Structure\":3,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S56\",\"Sentence\":\"Form 8594 if the business assets were sold as part of a structured sale\",\"HtmlTag\":\"li\",\"Structure\":2,\"Voice\":1,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S57\",\"Sentence\":\"Schedule SE if the business had net earnings of $400 or more during its final year\",\"HtmlTag\":\"li\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S58\",\"Sentence\":\"No Form 966 is required , because this LLC is not taxed as a corporation\",\"HtmlTag\":\"li\",\"Structure\":2,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S59\",\"Sentence\":\"Once these forms are filed and any payroll or information returns are closed, you can proceed with notifying the IRS to close the EIN.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":true,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S60\",\"Sentence\":\"Dissolving an LLC Taxed as a Partnership\",\"HtmlTag\":\"h2\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S61\",\"Sentence\":\"Multi-member LLCs are treated as partnerships for federal tax purposes unless they elected corporate taxation. When these LLCs dissolve, the IRS expects a formal partnership closure, including final reporting to each member and proper handling of asset dispositions.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S62\",\"Sentence\":\"When dissolving an LLC taxed as a partnership, you must file Form 1065 and clearly mark it as a \\u201CFinal return.\\u201D This signals to the IRS that the partnership has ended and will not file again.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S63\",\"Sentence\":\"In addition to the final partnership return, you must complete the following steps:\",\"HtmlTag\":\"p\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S64\",\"Sentence\":\"Issue final Schedule K-1s to all members, showing each partner\\u2019s share of income, losses, and distributions through the date of dissolution.\",\"HtmlTag\":\"li\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S65\",\"Sentence\":\"Report sales or dispositions of business assets on Form 4797 , including equipment, property, or intangible assets.\",\"HtmlTag\":\"li\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S66\",\"Sentence\":\"Use Form 8594 if the dissolution involved an asset sale structured across multiple categories.\",\"HtmlTag\":\"li\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S67\",\"Sentence\":\"File final payroll and information returns , including Forms 941 or 944, Form 940, W-2/W-3 for employees, and 1099/1096 for contractors, if applicable.\",\"HtmlTag\":\"li\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S68\",\"Sentence\":\"Form 966 is not required , because partnerships do not follow corporate dissolution rules.\",\"HtmlTag\":\"li\",\"Structure\":2,\"Voice\":1,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S69\",\"Sentence\":\"Every partner should receive their final tax documents on time, since missing or incorrect K-1s often trigger IRS notices or delays in personal tax filings.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":7,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S70\",\"Sentence\":\"Dissolving an LLC Taxed as a Corporation\",\"HtmlTag\":\"h2\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S71\",\"Sentence\":\"If your LLC elected to be taxed as a C-corporation or S-corporation, the IRS treats the dissolution like a corporate shutdown. This adds extra filing steps, tighter timelines, and specific reporting obligations that do not apply to sole proprietorships or partnerships.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S72\",\"Sentence\":\"LLCs taxed as corporations must notify the IRS that the business has formally adopted a plan to dissolve. This is done by filing Form 966 , which is mandatory for both C-corp and S-corp tax elections.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S73\",\"Sentence\":\"Here\\u2019s what the IRS requires:\",\"HtmlTag\":\"p\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S74\",\"Sentence\":\"File Form 966 within 30 days of the date the members or board approved the dissolution.\",\"HtmlTag\":\"li\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S75\",\"Sentence\":\"Attach the plan of dissolution or board resolution showing the decision to wind down the business.\",\"HtmlTag\":\"li\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S76\",\"Sentence\":\"File a final Form 1120 or Form 1120-S and clearly mark it as a \\u201CFinal return.\\u201D\",\"HtmlTag\":\"li\",\"Structure\":1,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S77\",\"Sentence\":\"Report asset sales or disposals using Form 4797 and Form 8594 , if applicable.\",\"HtmlTag\":\"li\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S78\",\"Sentence\":\"Report liquidating distributions to shareholders on their final K-1s or dividend statements.\",\"HtmlTag\":\"li\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S79\",\"Sentence\":\"Close all payroll and information returns , including Forms 941/944, 940, W-2/W-3, and 1099/1096, if the LLC had employees or contractors.\",\"HtmlTag\":\"li\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S80\",\"Sentence\":\"Missing Form 966 or filing it late is one of the most common IRS dissolution errors for LLCs taxed as corporations. Even if the business has stopped operating, the IRS will still expect filings until this step is completed correctly.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S81\",\"Sentence\":\"Avoid IRS Penalties With These Final Tips\",\"HtmlTag\":\"h2\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S82\",\"Sentence\":\"Most IRS issues during LLC dissolution happen not because founders skip everything, but because one small step is missed or done out of order. These final checks help you close the business cleanly and avoid penalties:-\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":6,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S83\",\"Sentence\":\"i) Match Your Tax Status to IRS Requirements\",\"HtmlTag\":\"h3\",\"Structure\":4,\"Voice\":0,\"InfoQuality\":3,\"FunctionalType\":0,\"InformativeType\":7,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S84\",\"Sentence\":\"Before filing anything, confirm how your LLC is taxed with the IRS. Many founders assume their tax status based on formation, but elections may have changed over time. Check past filings to confirm whether your LLC is treated as a sole proprietorship, partnership, or corporation. Filing the wrong final return is one of the fastest ways to trigger follow-up notices.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":2,\"FunctionalType\":0,\"InformativeType\":7,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S85\",\"Sentence\":\"ii) Align Legal and IRS Dissolution Dates\",\"HtmlTag\":\"h3\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":7,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S86\",\"Sentence\":\"State dissolution and IRS closure should be coordinated. If the IRS sees activity after your legal dissolution date, or vice versa, it may expect another return.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":7,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S87\",\"Sentence\":\"Keep these aligned:\",\"HtmlTag\":\"p\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":7,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":true,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S88\",\"Sentence\":\"File state dissolution close to the date you stop business activity\",\"HtmlTag\":\"li\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":7,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S89\",\"Sentence\":\"Use the same closure period on your final IRS returns\",\"HtmlTag\":\"li\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":7,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S90\",\"Sentence\":\"Avoid dissolving late in the year if it unnecessarily creates another tax year\",\"HtmlTag\":\"li\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":7,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S91\",\"Sentence\":\"Ensure all payroll and contractor filings end on the same timeline\",\"HtmlTag\":\"li\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":7,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S92\",\"Sentence\":\"Consistency across dates reduces confusion and prevents extra filing obligations.\",\"HtmlTag\":\"p\",\"Structure\":1,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S93\",\"Sentence\":\"iii) Take Help to Avoid Filing Errors\",\"HtmlTag\":\"h3\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":7,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S94\",\"Sentence\":\"LLC dissolution involves multiple forms, deadlines, and dependencies that are easy to miss. Using a platform like Inkle or working with a CPA helps ensure Form 966 is filed on time when required, final K-1s are accurate, asset sales are reported correctly, and EIN closure is handled properly. This reduces the risk of penalties and removes the stress of tracking every requirement manually.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":1,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":1,\"EntityMentionFlag\":null},{\"SentenceId\":\"S95\",\"Sentence\":\"How to Use Inkle for LLC Dissolution\",\"HtmlTag\":\"h2\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":1,\"InformativeType\":8,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S96\",\"Sentence\":\"You need to coordinate final tax returns, payroll closures, asset reporting, and IRS notifications while closing an LLC. Inkle helps founders manage this entire process in one place, reducing the risk of errors that can keep an LLC open in IRS records.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":1,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":1,\"EntityMentionFlag\":null},{\"SentenceId\":\"S97\",\"Sentence\":\"Inkle guides you through each dissolution step based on how your LLC is taxed. The platform prepares final federal tax returns, ensures the correct \\u201CFinal return\\u201D indicators are applied, and helps generate the documents required for closure. For LLCs taxed as corporations, Inkle supports Form 966 preparation and ensures it is filed within the required timeline.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":2,\"FunctionalType\":0,\"InformativeType\":1,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":1,\"EntityMentionFlag\":null},{\"SentenceId\":\"S98\",\"Sentence\":\"Beyond tax forms, Inkle helps founders prepare EIN closure requests, organize dissolution documents, and retain records for future reference. Everything stays tracked and archived so you are not searching for paperwork months later if the IRS or another authority follows up.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":1,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":1,\"EntityMentionFlag\":null},{\"SentenceId\":\"S99\",\"Sentence\":\"So many founders use Inkle because it handles complexity without forcing them to manage multiple advisors or spreadsheets.\",\"HtmlTag\":\"p\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":1,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S100\",\"Sentence\":\"Supports final Schedule C, Form 1065, Form 1120, and Form 1120-S filings\",\"HtmlTag\":\"li\",\"Structure\":1,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":1,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S101\",\"Sentence\":\"Flags IRS deadlines and required forms based on tax classification\",\"HtmlTag\":\"li\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":2,\"FunctionalType\":0,\"InformativeType\":1,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S102\",\"Sentence\":\"Ensures asset sales and liquidation reporting are handled correctly\",\"HtmlTag\":\"li\",\"Structure\":2,\"Voice\":1,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":1,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S103\",\"Sentence\":\"Maintains organized document storage for audits or future verification\",\"HtmlTag\":\"li\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":1,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S104\",\"Sentence\":\"Provides CPA-backed support for reviewing dissolution filings\",\"HtmlTag\":\"li\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":1,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S105\",\"Sentence\":\"Book a free demo with Inkle to avoid penalties, missed filings, or an extra tax year.. The session walks through how final returns, Form 966, EIN closure, and documentation are handled step by step, so you can shut down your LLC cleanly and move on without loose ends.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":7,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":1,\"EntityMentionFlag\":null},{\"SentenceId\":\"S106\",\"Sentence\":\"Frequently Asked Questions\",\"HtmlTag\":\"h2\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":1,\"FunctionalType\":0,\"InformativeType\":9,\"ClaritySynthesisType\":3,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":false,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S107\",\"Sentence\":\"What IRS forms are required to dissolve an LLC?\",\"HtmlTag\":\"h3\",\"Structure\":0,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":1,\"InformativeType\":8,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S108\",\"Sentence\":\"The forms depend on how your LLC is taxed. Single-member LLCs file Schedule C with Form 1040. Partnerships file Form 1065. LLCs taxed as corporations file Form 1120 or 1120-S. Corporate-taxed LLCs may also need to file Form 966.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S109\",\"Sentence\":\"When do I file Form 966 for an LLC?\",\"HtmlTag\":\"h3\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":3,\"FunctionalType\":1,\"InformativeType\":8,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S110\",\"Sentence\":\"Form 966 is required only if your LLC elected to be taxed as a C-corporation or S-corporation. It must be filed within 30 days of approving the dissolution plan. Sole proprietorships and partnerships do not file Form 966.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S111\",\"Sentence\":\"How do I close my EIN with the IRS while dissolving LLC?\",\"HtmlTag\":\"h3\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":3,\"FunctionalType\":1,\"InformativeType\":8,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S112\",\"Sentence\":\"After filing all final tax returns, you must send a written request to the IRS asking to close the EIN. The letter should include the LLC\\u2019s legal name, EIN, business address, and the reason for closure. EINs are not closed automatically.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null},{\"SentenceId\":\"S113\",\"Sentence\":\"What happens if I skip final tax returns while LLC dissolution?\",\"HtmlTag\":\"h3\",\"Structure\":2,\"Voice\":0,\"InfoQuality\":3,\"FunctionalType\":1,\"InformativeType\":8,\"ClaritySynthesisType\":1,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S114\",\"Sentence\":\"If final returns are not filed, the IRS may treat the LLC as still active. This can lead to penalties, late notices, and a requirement to file additional returns for future tax years.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S115\",\"Sentence\":\"Can I dissolve my LLC at the state level first?\",\"HtmlTag\":\"h3\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":3,\"FunctionalType\":1,\"InformativeType\":8,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S116\",\"Sentence\":\"Yes, but IRS filings must still be completed correctly. State dissolution does not replace federal tax closure. If the IRS steps are not aligned with the legal shutdown, you may trigger extra filing obligations.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S117\",\"Sentence\":\"Does the IRS care about my state dissolution date?\",\"HtmlTag\":\"h3\",\"Structure\":0,\"Voice\":0,\"InfoQuality\":3,\"FunctionalType\":1,\"InformativeType\":8,\"ClaritySynthesisType\":0,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":0,\"EntityMentionFlag\":null},{\"SentenceId\":\"S118\",\"Sentence\":\"The IRS focuses on when business activity stopped and how final returns are filed. Using consistent dates across state filings and IRS forms helps avoid confusion and follow-up notices.\",\"HtmlTag\":\"p\",\"Structure\":3,\"Voice\":1,\"InfoQuality\":0,\"FunctionalType\":0,\"InformativeType\":0,\"ClaritySynthesisType\":2,\"ClaimsCitation\":false,\"IsGrammaticallyCorrect\":true,\"HasPronoun\":false,\"IsPlagiarized\":false,\"ParagraphId\":\"\",\"RelevanceScore\":0,\"AnswerSentenceFlag\":0,\"EntityConfidenceFlag\":0,\"Source\":3,\"EntityMentionFlag\":null}]";
       //var geminiTags = JsonSerializer.Deserialize<List<GeminiSentenceTag>>(serialized);
        var geminiTags = await _gemini.TagArticleAsync(SentenceTaggingPrompts.GeminiSentenceTagPrompt, JsonSerializer.Serialize(sentences),"gemini:tagging");
       //geminiTags.RemoveAll(x => x == null ||x.Sentence == null);
        geminiTags?.ToList()?.ForEach(g =>
        {if(g?.Sentence != null)
            {
                var sentence = fullLocalLlmTags.Sentences.FirstOrDefault(s => s.SentenceId == g.SentenceId);
                if(sentence!= null)
                {
                    g.Sentence = sentence?.Sentence ?? string.Empty;
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
            
        });

        var anyMismatch = geminiTags?.Where(gm =>
        {
            var gq = fullLocalLlmTags.Sentences?.FirstOrDefault(x => x.Sentence == gm.Sentence);
            if (gq == null || gm == null) return true;
            if (gq.InformativeType != gm.InformativeType)
            {
                return true;
            }
            return false;
        }).ToList();

        List<ChatgptGeminiSentenceTag>? chatGptDecisions = new List<ChatgptGeminiSentenceTag>();
       
        if (anyMismatch != null && anyMismatch.Count>0)
        {

            var tasks = new List<Task<List<ChatgptGeminiSentenceTag>>>();
            var batchSize = 150;
            for (int i=0;i< anyMismatch.Count; i+=batchSize)
            {
                tasks.Add(HandleMismatchSentences(anyMismatch.Skip(i).Take(batchSize).ToList(), geminiTags, fullLocalLlmTags.Sentences, chatGptDecisions));               
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

        var validated = new Phase1And2Orchestrator().Execute(sentences, fullLocalLlmTags.Sentences, geminiTags, chatGptDecisions);

        await Task.WhenAll(intentScoreTask,keywordScoreTask);
        var intentScore = await intentScoreTask;
        var keywordScore = await keywordScoreTask;
        return new OrchestratorResponse()
        {
            ValidatedSentences = validated,
            //PlagiarismScore = await _gemini.GetPlagiarismScore(sentences),
            SectionScore = sectionScore,//doesn't require informativetype
            IntentScore = intentScore, //doesn;t require informative type
            KeywordScore = keywordScore, //doesn't require informativeType
            AnswerPositionIndex = fullLocalLlmTags.AnswerPositionIndex?? await GetAnswerPositionIndex(validated?.ToList(), request)
        };
    }

    public static List<Section> BuildSections(List<GeminiSentenceTag> sentences)
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
                    SectionText = sentence.Sentence,
                    SentenceIds = new List<string>()
                };
            }
            else
            {
                // Content sentence → attach to current section
                if (currentSection != null)
                {
                    currentSection.SentenceIds.Add(sentence.SentenceId);
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
        while (!done && retryCount<1)
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

    public async Task<double> GetKeywordScore(List<GeminiSentenceTag> validated, SeoRequest request)
    {
        var response = await GetSectionScoreResAsync(request.PrimaryKeyword);
        var h1 = validated.FirstOrDefault(vs => vs.HtmlTag.ToLower() == "h1")?.Sentence ?? string.Empty;
        var h2 = validated.Where(vs => vs.HtmlTag.ToLower() == "h2")?.Select(x => x.Sentence)?.ToList();
        var h3 = validated.Where(vs => vs.HtmlTag.ToLower() == "h3")?.Select(x => x.Sentence)?.ToList();
        var body = string.Join("",validated.Where(vs => vs.HtmlTag.ToLower() != "h1" && vs.HtmlTag.ToLower() != "h2" && vs.HtmlTag.ToLower() != "h3")?.Select(x => x.Sentence).ToList()) ?? string.Empty;

        var h2h3list = new List<string>();
        if (h2 != null && h2.Count > 0)
            h2h3list.AddRange(h2);
        if (h3 != null && h3.Count > 0)
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

    public async Task<double> GetIntentScoreInfo(string keyword)
    {
        var response = await GetSectionScoreResAsync(keyword);
        int match = 0;
        response.Competitors?.ToList()?.ForEach(c =>
        {
            if (c.Intent == response.Intent)
                match += 1;
        });
        var intentScore = ((double)match / response.Competitors.Count);
        // return SectionScorer.Calculate(response?.Competitors, myHeadings);
        return intentScore*10;
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
                    try
                    {
                        //var data = File.ReadAllText($"SectionScoreResponse_{keyword}.json");
                        //if (data != null)
                        //{
                        //    return JsonSerializer.Deserialize<SectionScoreResponse>(data, options);
                        //}

                    }
                    catch
                    {

                    }

                    var res = await _gemini.GetSectionScore(keyword);
                    if (!string.IsNullOrEmpty(res))
                    {
                        File.WriteAllText($"SectionScoreResponse_{keyword}.json", res);
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
    public async Task<double> GetSectionScoreInfo(string keyword,List<string> secondaryKeywords, List<GeminiSentenceTag> validatedSentences)
    {
        var options = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };
        var response = await GetSectionScoreResAsync(keyword);
        //var response = System.Text.Json.JsonSerializer.Deserialize<List<CompetitorSectionScoreResponse>>("[{\"Url\":\"https://www.irs.gov/businesses/small-businesses-self-employed/am-i-required-to-file-a-form-1099-or-other-information-return\",\"Headings\":[\"Am I required to file a Form 1099 or other information return?\",\"Made a payment\",\"For each person to whom you have paid at least $600 for the following during the year (Form 1099-NEC):\"],\"Intent\":0},{\"Url\":\"https://turbotax.intuit.com/tax-tips/tax-payments/what-is-an-irs-1099-form/L7f5lR9C7\",\"Headings\":[\"What Is an IRS 1099 Form?\",\"Who receives a 1099 Form?\",\"Some common examples when you might receive a 1099 for 2025 in 2026 include:\",\"Due Date to IRS\"],\"Intent\":0},{\"Url\":\"https://www.hrblock.com/tax-center/income/other-income/form-1099-k/\",\"Headings\":[\"Form 1099-K: Definition, uses, and who has a reporting requirement\",\"At a glance\",\"How you report 1099-K income depends on why you got it\"],\"Intent\":0},{\"Url\":\"https://www.xero.com/us/small-business-guides/tax-accounting/1099-nec-filing/\",\"Headings\":[\"1099-NEC filing requirements: Contractor payment reporting guide for 2026\",\"Key takeaways\",\"The $600 threshold and how to apply it\"],\"Intent\":0},{\"Url\":\"https://www.patriotsoftware.com/blog/payroll/1099-state-filing-requirements/\",\"Headings\":[\"1099 State Filing Requirements\",\"IRS deadline reminders\"],\"Intent\":0}]",options);
        List<string> myHeadings = validatedSentences.Where(vs => vs.HtmlTag.ToLower() == "h2").Select(x => x.Sentence).ToList();

        var cHeadings = new List<string>();
        response.Competitors?.ForEach(c =>
        {
            cHeadings.AddRange(c.Headings);
        });
        cHeadings.AddRange(myHeadings);
        var batchSize = 30;
        var finalRes = new List<string>();
        for(int i=0;i<cHeadings.Count;i+=batchSize)
        {
            var data = await _groq.GetGroqCategorization(cHeadings);
            if(data!= null && data.Count>0)
               finalRes.AddRange(data);
        }
        
        return SectionScorer.Calculate(response?.Competitors,keyword, finalRes);
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

    public async Task<RecommendationResponseDTO> GetFullRecommendationsAsync(string article, List<GeminiSentenceTag> level1, List<Section> sections)
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
                //Sections=sections,
                Sentences = level1.Select(x => new
                {
                    Id = x.SentenceId,
                    Text = x.Sentence,
                    HtmlTag = x.HtmlTag,
                    //ParagraphId = x.ParagraphId,
                }).ToList()
            });
            response.Recommendations = await GenerateRecommendationsAsync(request);
            
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