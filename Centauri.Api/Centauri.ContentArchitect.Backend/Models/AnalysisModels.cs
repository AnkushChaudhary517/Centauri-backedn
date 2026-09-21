namespace Centauri.ContentArchitect.Backend.Models;

public sealed record AnalysisRequest(
    string PrimaryKeyword,
    string TargetRegion,
   
    string TargetUrl,
     string? Language="en",
    IReadOnlyList<string>? Competitors = null,
    IReadOnlyList<UserMaterial>? UserMaterials = null);

public sealed record UserMaterial(string Name, string Content);

public sealed class AnalysisResponse
{
    public string AnalysisId { get; set; } = "";
    public string PrimaryKeyword { get; set; } = "";
    public string TargetRegion { get; set; } = "";
    public string? Language { get; set; } = "en";
    public string Targeturl { get; set; } = "";
    public AnalysisConfidence? Confidence { get; set; } = new();
    public FoundationalData? Foundational { get; set; } = new();
    public IntermediateMetrics? Intermediate { get; set; } = new();
    public MetricResults? Metrics { get; set; } = new();
}

public sealed class AnalysisErrorResponse
{
    public string Error { get; set; } = "";
    public string StackTrace { get; set; } = "";
}

public sealed class AnalysisConfidence
{
    public string Overall { get; set; } = "Unknown";
    public Dictionary<string, string> ByMetric { get; set; } = new Dictionary<string, string>();
    public List<string> Warnings { get; set; } = new List<string>();
}

public sealed class FoundationalData
{
    public KeywordData Keyword { get; set; } = new KeywordData();
    public List<SerpResult> Top10 { get; set; } = new List<SerpResult>();
    public List<QuestionItem> PaaQuestions { get; set; } = new List<QuestionItem>();
    public List<string> RelatedSearches { get; set; } = new List<string>();
    public List<CompetitorAnalysis> Competitors { get; set; } = new List<CompetitorAnalysis>();
    public SiteIndexData SiteIndex { get; set; } = new SiteIndexData();
    public ContentAnalysisAggregate ContentAnalysis { get; set; } = new ContentAnalysisAggregate();
}

public sealed class KeywordData
{
    public double PrimarySearchVolume { get; set; }
    public double PrimaryCpc { get; set; }
    public string PrimaryCompetitionLevel { get; set; } = "";
    public double PrimaryCompetitionIndex { get; set; }
    public double PrimaryLowTopOfPageBid { get; set; }
    public double PrimaryHighTopOfPageBid { get; set; }
    public List<MonthlySearchVolume> PrimaryMonthlySearches { get; set; } = new();
    public List<KeywordCluster> SecondaryClusters { get; set; } = new List<KeywordCluster>();
    public string PrimaryIntent { get; set; } = "unknown";
    public List<string> PrimarySupplementaryIntents { get; set; } = new();
    public string PrimaryIntentLastUpdatedTime { get; set; } = "";
}

public sealed class KeywordCluster
{
    public string CanonicalKeyword { get; set; } = "";
    public double Volume { get; set; }
    public List<string> Variants { get; set; } = new List<string>();
}

public sealed class SerpResult
{
    public int Position { get; set; }
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Domain { get; set; } = "";
    public int ReferringDomains { get; set; }
    public double DomainStrength { get; set; }
    public double ContentCoverage { get; set; }
    public double IntentMatch { get; set; }
    public bool HasAds { get; set; }
    public bool HasPaa { get; set; }
    public bool HasVideo { get; set; }
    public bool HasFeaturedSnippet { get; set; }
    public bool HasAiOverview { get; set; }
    public double FirstHandEvidenceRate { get; set; }
    public double SourceDensity { get; set; }
    public List<string> Questions { get; set; } = new List<string>();
    public List<string> Entities { get; set; } = new List<string>();
    public string ExtractedText { get; set; } = "";
}

public sealed class QuestionItem
{
    public string Question { get; set; } = "";
    public string Source { get; set; } = "";
}

public sealed class CompetitorAnalysis
{
    public string Url { get; set; } = "";
    public double DomainStrength { get; set; }
    public int ReferringDomains { get; set; }
    public double ContentCoverage { get; set; }
    public double IntentMatch { get; set; }
    public List<string> Questions { get; set; } = new List<string>();
    public List<string> Entities { get; set; } = new List<string>();
    public double FirstHandEvidenceRate { get; set; }
    public double SourceDensity { get; set; }
}

public sealed class SiteIndexData
{
    public int SitemapUrlCount { get; set; }
    public int InspectedUrls { get; set; }
    public int IndexedPassCount { get; set; }
    public double HistoricalIndexRate { get; set; }
    public double CrawlHealth { get; set; }
    public double CanonicalConsistency { get; set; }
    public double SitemapHealth { get; set; }
    public double InternalDiscovery { get; set; }
    public double DomainStrength { get; set; }
    public bool IsEstimated { get; set; }
    public string Source { get; set; } = "SearchConsole";
}

public sealed class ContentAnalysisAggregate
{
    public double FirstHandEvidenceRate { get; set; }
    public double OriginalDataPrevalence { get; set; }
    public double SourceRequirement { get; set; }
    public double AuthorityPressure { get; set; }
    public double YmyLTopicSensitivity { get; set; }
    public double FreshnessRequirement { get; set; }
    public double CompetitorRedundancy { get; set; }
    public double MissingQuestionCoverage { get; set; }
    public double MissingEntityCoverage { get; set; }
    public double MissingEvidenceCoverage { get; set; }
}

public sealed class IntermediateMetrics
{
    public double AddressableSearchDemand { get; set; }
    public double AuthorityPressure { get; set; }
    public double LinkPressure { get; set; }
    public double IntentSaturation { get; set; }
    public double CompetitorContentStrength { get; set; }
    public double IndexabilityReadiness { get; set; }
    public double SerpClickability { get; set; }
    public double QuestionCoverageAverage { get; set; }
    public double EvidenceRequirement { get; set; }
    public double InformationGainOpportunity { get; set; }
}

public sealed class MetricResults
{
    public SearchVolumeResult TotalSearchVolume { get; set; } = new();
    public KeywordDifficultyResult KeywordDifficulty { get; set; } = new();
    public IndexabilityResult Indexability { get; set; } = new();
    public TrafficPotentialResult TrafficPotential { get; set; } = new();
    public QuestionsAnsweredResult QuestionsAnswered { get; set; } = new();
    public ContentGapsResult AdditionalQuestions { get; set; } = new();
    public EeatResult EeatInformationGain { get; set; } = new();
}

public sealed class SearchVolumeResult
{
    public double PrimaryVolume { get; set; }
    public double AddressableSearchDemand { get; set; }
    public List<KeywordCluster> DeduplicatedClusters { get; set; } = new List<KeywordCluster>();
}
public sealed class KeywordDifficultyResult
{
    public double Score { get; set; }
    public string Label { get; set; } = "";
    public double AuthorityPressure { get; set; }
    public double LinkPressure { get; set; }
    public double IntentSaturation { get; set; }
    public double CompetitorContentStrength { get; set; }
}
public sealed class IndexabilityResult
{
    public double ReadinessScore { get; set; }
    public string Label { get; set; } = "";
    public bool IsProbability { get; set; }
    public bool IsEstimated { get; set; }
    public string Source { get; set; } = "SearchConsole";
    public double? Probability14Days { get; set; }
}
public sealed class TrafficPotentialResult
{
    public List<TrafficScenario> Scenarios { get; set; } = new List<TrafficScenario>();
}
public sealed class TrafficScenario
{
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public double EstimatedMonthlyTraffic { get; set; }
}
public sealed class QuestionsAnsweredResult
{
    public List<QuestionCoverageResult> Questions { get; set; } = new List<QuestionCoverageResult>();
}
public sealed class QuestionCoverageResult
{
    public string Question { get; set; } = "";
    public double Coverage { get; set; }
    public string Classification { get; set; } = "";
}
public sealed class ContentGapsResult
{
    public List<ContentGapResult> Gaps { get; set; } = new List<ContentGapResult>();
    public List<ContentGapResult> TopQuestions { get; set; } = new List<ContentGapResult>();
}
public sealed class ContentGapResult
{
    public string Question { get; set; } = "";
    public double IntentRelevance { get; set; }
    public double DemandProxy { get; set; }
    public double CompetitorGap { get; set; }
    public double Uniqueness { get; set; }
    public double Score { get; set; }
    public string Classification { get; set; } = "";
}

public sealed class GenerateOutlineRequest
{
    public string AnalysisId { get; set; } = "";
    public string PrimaryKeyword { get; set; } = "";
    public string CountryOrRegion { get; set; } = "";
    public string Language { get; set; } = "";
    public string WebsiteUrl { get; set; } = "";
    public string UserInput { get; set; } = "";
    public List<string> SelectedCompetitorQuestions { get; set; } = new();
    public List<string> SelectedAdditionalQuestions { get; set; } = new();
}

public sealed class GeneratedOutline
{
    public string Title { get; set; } = "";
    public string MetaDescription { get; set; } = "";
    public List<OutlineSection> Sections { get; set; } = new();
}

public sealed class OutlineSection
{
    public string Heading { get; set; } = "";
    public string Purpose { get; set; } = "";
    public List<string> QuestionsToAnswer { get; set; } = new();
    public List<string> KeyPoints { get; set; } = new();
}
public sealed class EeatResult
{
    public double EvidenceRequirement { get; set; }
    public double InformationGainOpportunity { get; set; }
    public double CompositeScore { get; set; }
    public string EvidenceExpected { get; set; } = "";
    public string InformationGainExpected { get; set; } = "";
    public List<string> RecommendedEvidenceTypes { get; set; } = new List<string>();
}

public sealed class PageAnalysis
{
    public string Url { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> Headings { get; set; } = new List<string>();
    public List<string> Questions { get; set; } = new List<string>();
    public List<string> Entities { get; set; } = new List<string>();
    public double FirstHandEvidenceRate { get; set; }
    public double OriginalDataPrevalence { get; set; }
    public double SourceDensity { get; set; }
}
