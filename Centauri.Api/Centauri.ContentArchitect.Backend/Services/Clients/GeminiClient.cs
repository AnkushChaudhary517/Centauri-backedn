using System.Text.Json;
using Centauri.ContentArchitect.Backend.Configuration;
using Centauri.ContentArchitect.Backend.Models;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centauri.ContentArchitect.Backend.Services.Clients;

public sealed class GeminiClient : IGeminiClient
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiClient> _logger;
    private readonly string _gcpProject;
    private readonly string _gcpLocation;
    private readonly string _modelDefault;
    private readonly string _modelTagging;
    private readonly Lazy<Task<GoogleCredential>> _credential;

    public GeminiClient(
        IHttpClientFactory factory,
        IOptions<GeminiOptions> options,
        IConfiguration config,
        ILogger<GeminiClient> logger)
    {
        _http = factory.CreateClient();
        _options = options.Value;
        _logger = logger;
        _gcpProject = config["Gemini:ProjectId"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
            ?? Environment.GetEnvironmentVariable("GCLOUD_PROJECT")
            ?? "gen-lang-client-0445687823";
        _gcpLocation = config["Gemini:Location"] ?? "asia-south1";
        _modelDefault = config["Gemini:Model:Default"] ?? "gemini-2.5-flash";
        _modelTagging = config["Gemini:Model:Tagging"] ?? "gemini-2.5-flash";
        _credential = new Lazy<Task<GoogleCredential>>(CreateCredentialAsync);

        if (_gcpProject == "gen-lang-client-0445687823")
            _logger.LogWarning("No Google project configured for Gemini; using fallback project which may not be accessible.");
    }

    public Task<AiPageAnalysis> AnalyzePageAsync(string keyword, string pageText, string headings, CancellationToken ct)
        => GenerateJsonAsync<AiPageAnalysis>(
            "Analyze this ranking page for SEO research.\n" +
            $"Keyword: {keyword}\n" +
            $"Headings: {headings}\n" +
            $"Page text: {Trim(pageText, 30000)}\n" +
            "Return JSON only matching this schema:\n" +
            "intent:string, contentCoverage:number 0..100, intentMatch:number 0..1,\n" +
            "questions:string[], entities:string[], firstHandEvidenceRate:number 0..1,\n" +
            "originalDataPrevalence:number 0..1, sourceRequirement:number 0..1,\n" +
            "ymyLSensitivity:number 0..1, freshnessRequirement:number 0..1,\n" +
            "evidenceTypes:string[], semanticSimilarityToOtherPages:number 0..1.", _modelDefault,
            ct);

    public Task<AiQuestionAnalysis> AnalyzeQuestionsAsync(string keyword, IReadOnlyList<string> questions, string pageCorpus, CancellationToken ct)
    {
        return GenerateJsonAsync<AiQuestionAnalysis>(
            "Normalize and evaluate candidate questions for keyword \"" + keyword + "\".\n" +
            "Questions:\n" +
            string.Join("\n", questions.Select((q, i) => $"{i + 1}. {q}")) + "\n" +
            "Ranking-page corpus:\n" +
            Trim(pageCorpus, 30000) + "\n" +
            "Return JSON only: {\"questions\":[{\"question\":\"...\",\"answered\":true/false,\"intentRelevance\":0..1,\"demandProxy\":0..1,\"uniqueness\":0..1}]}", _modelTagging,
            ct);
    }

    public async Task<double> CalculateSemanticSimilarityAsync(string textA, string textB, CancellationToken ct)
    {
        var r = await GenerateJsonAsync<SimilarityResponse>(
            "Calculate semantic similarity between these two documents.\n" +
            "Return JSON only: {\"similarity\":0..1}\n" +
            $"A: {Trim(textA, 12000)}\n" +
            $"B: {Trim(textB, 12000)}", _modelDefault,
            ct);
        return Math.Clamp(r.Similarity, 0, 1);
    }

    public Task<AiPageAnalysis> AnalyzeSiteContentAsync(string keyword, string siteText, CancellationToken ct)
        => AnalyzePageAsync(keyword, siteText, "", ct);

    public Task<GeneratedOutline> GenerateOutlineAsync(
        AnalysisResponse analysis,
        IReadOnlyList<string> selectedCompetitorQuestions,
        IReadOnlyList<string> selectedAdditionalQuestions,
        string userInput,
        CancellationToken ct)
    {
        var keyword = analysis.PrimaryKeyword;
        var clusters = analysis.Foundational.Keyword.SecondaryClusters
            .Take(20)
            .Select(x => x.CanonicalKeyword);
        var competitors = analysis.Foundational.Top10
            .Take(10)
            .Select(x => $"- {x.Title} ({x.Url})");

        var expertPerspective = string.IsNullOrWhiteSpace(userInput)
            ? ""
            : $"\nUser's expert viewpoint to incorporate: {userInput}\n";

        return GenerateJsonAsync<GeneratedOutline>(
            "Create a complete SEO content outline. Return JSON only matching this schema: " +
            "{title:string,metaDescription:string,sections:[{heading:string,purpose:string,questionsToAnswer:string[],keyPoints:string[]}]}.\n" +
            $"Primary keyword: {keyword}\n" +
            $"Website: {analysis.Targeturl}\n" +
            "Secondary keywords:\n" + string.Join("\n", clusters) + "\n" +
            "People-also-ask questions:\n" + string.Join("\n", analysis.Foundational.PaaQuestions.Select(x => x.Question).Take(20)) + "\n" +
            "Related searches:\n" + string.Join("\n", analysis.Foundational.RelatedSearches.Take(20)) + "\n" +
            "Top-ranking pages:\n" + string.Join("\n", competitors) + "\n" +
            "Selected questions competitors already answer. Cover these with a clearer, more useful angle:\n" +
            string.Join("\n", selectedCompetitorQuestions) + "\n" +
            "Selected additional questions. Include every one naturally in the outline:\n" +
            string.Join("\n", selectedAdditionalQuestions) +
            expertPerspective,
            _modelDefault,
            ct);
    }

    private async Task<T> GenerateJsonAsync<T>(string prompt, string model, CancellationToken ct)
    {
        EnsureEnabled();
        var accessToken = await GetAccessTokenAsync();
        var url = $"https://{_gcpLocation}-aiplatform.googleapis.com/v1/projects/{Uri.EscapeDataString(_gcpProject)}/locations/{Uri.EscapeDataString(_gcpLocation)}/publishers/google/models/{Uri.EscapeDataString(model)}:generateContent";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = JsonContent.Create(new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
            generationConfig = new { responseMimeType = "application/json", temperature = 0.1 }
        });
        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("candidates")[0]
            .GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "{}";
        return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static async Task<GoogleCredential> CreateCredentialAsync()
        => (await GoogleCredential.GetApplicationDefaultAsync())
            .CreateScoped("https://www.googleapis.com/auth/cloud-platform");

    private async Task<string> GetAccessTokenAsync()
        => await (await _credential.Value).UnderlyingCredential.GetAccessTokenForRequestAsync();

    private void EnsureEnabled()
    {
        if (!_options.Enabled) throw new InvalidOperationException("Gemini is disabled in appsettings.");
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];
    private sealed class SimilarityResponse { public double Similarity { get; set; } }
}
