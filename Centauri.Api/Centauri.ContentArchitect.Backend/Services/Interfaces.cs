using Centauri.ContentArchitect.Backend.Models;

namespace Centauri.ContentArchitect.Backend.Services;

public interface IContentArchitectService
{
    Task<AnalysisResponse> AnalyzeAsync(AnalysisRequest request, CancellationToken cancellationToken);
}
public interface IKeywordDataClient
{
    Task<KeywordApiResult> GetKeywordDataAsync(string keyword, string country, string language, CancellationToken ct);
    Task<KeywordIntentData> GetKeywordIntentAsync(string keyword, string country, string language, CancellationToken ct);
}
public interface ISerpDataClient
{
    Task<SerpApiResult> GetSerpAsync(string keyword, string country, string language, CancellationToken ct);
}
public interface IBacklinkDataClient
{
    Task<(int ReferringDomains, double DomainStrength)> GetBacklinkDataAsync(string url, CancellationToken ct);
}
public interface ISearchConsoleClient
{
    Task<List<GscInspection>> InspectUrlsAsync(string siteUrl, IReadOnlyList<string> urls, CancellationToken ct);
}
public interface IPublicIndexabilityClient
{
    Task<List<PublicIndexabilityInspection>> InspectUrlsAsync(string websiteUrl, IReadOnlyList<string> urls, CancellationToken ct);
}
public interface IGeminiClient
{
    Task<AiPageAnalysis> AnalyzePageAsync(string keyword, string pageText, string headings, CancellationToken ct);
    Task<AiQuestionAnalysis> AnalyzeQuestionsAsync(string keyword, IReadOnlyList<string> questions, string pageCorpus, CancellationToken ct);
    Task<double> CalculateSemanticSimilarityAsync(string textA, string textB, CancellationToken ct);
    Task<AiPageAnalysis> AnalyzeSiteContentAsync(string keyword, string siteText, CancellationToken ct);
    Task<GeneratedOutline> GenerateOutlineAsync(
        AnalysisResponse analysis,
        IReadOnlyList<string> selectedCompetitorQuestions,
        IReadOnlyList<string> selectedAdditionalQuestions,
        string userInput,
        CancellationToken ct);
}
public interface IWebPageParser
{
    Task<PageAnalysis> ParseAsync(string url, CancellationToken ct);
}
public interface ISitemapService
{
    Task<List<string>> GetUrlsAsync(string websiteUrl, int maxUrls, CancellationToken ct);
}
public interface IKeywordCalculator
{
    SearchVolumeResult Calculate(KeywordData keywordData);
}
