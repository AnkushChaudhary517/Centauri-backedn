namespace Centauri.ContentArchitect.Backend.Models;

public sealed class KeywordApiResult
{
    public double SearchVolume { get; set; }
    public double Cpc { get; set; }
    public double Competition { get; set; }
    public string CompetitionLevel { get; set; } = "";
    public double LowTopOfPageBid { get; set; }
    public double HighTopOfPageBid { get; set; }
    public List<MonthlySearchVolume> MonthlySearches { get; set; } = new();
    public List<KeywordIdea> Ideas { get; set; } = new List<KeywordIdea>();
    public bool SearchPartners { get; set; }
    public int LocationCode { get; set; }
    public string LanguageCode { get; set; } = "";
    public List<string> Concepts { get; set; } = new();
}

public sealed class KeywordIntentData
{
    public string MainIntent { get; set; } = "unknown";
    public List<string> SupplementaryIntents { get; set; } = new();
    public string LastUpdatedTime { get; set; } = "";
}
public sealed class KeywordIdea
{
    public string Keyword { get; set; } = "";
    public double SearchVolume { get; set; }
    public double Cpc { get; set; }
    public string CompetitionLevel { get; set; } = "";
    public double CompetitionIndex { get; set; }
    public double LowTopOfPageBid { get; set; }
    public double HighTopOfPageBid { get; set; }
    public List<MonthlySearchVolume> MonthlySearches { get; set; } = new();
    public List<string> Concepts { get; set; } = new();
    public bool SearchPartners { get; set; }
}

public sealed class MonthlySearchVolume
{
    public int Year { get; set; }
    public int Month { get; set; }
    public double SearchVolume { get; set; }
}
public sealed class SerpApiResult
{
    public List<SerpResult> OrganicResults { get; set; } = new List<SerpResult>();
    public List<QuestionItem> PeopleAlsoAsk { get; set; } = new List<QuestionItem>();
    public List<string> RelatedSearches { get; set; } = new List<string>();
    public bool HasAiOverview { get; set; }
    public bool HasAds { get; set; }
    public bool HasVideo { get; set; }
    public bool HasFeaturedSnippet { get; set; }
}
public sealed class GscInspection
{
    public string Url { get; set; } = "";
    public bool Indexed { get; set; }
    public bool CrawlOk { get; set; }
    public bool CanonicalConsistent { get; set; }
}

public sealed class PublicIndexabilityInspection
{
    public string Url { get; set; } = "";
    public bool CrawlOk { get; set; }
    public bool IsBlockedByRobots { get; set; }
    public bool HasNoindex { get; set; }
    public bool CanonicalConsistent { get; set; }
}
public sealed class AiPageAnalysis
{
    public string Intent { get; set; } = "unknown";
    public double ContentCoverage { get; set; }
    public double IntentMatch { get; set; }
    public List<string> Questions { get; set; } = new List<string>();
    public List<string> Entities { get; set; } = new List<string>();
    public double FirstHandEvidenceRate { get; set; }
    public double OriginalDataPrevalence { get; set; }
    public double SourceRequirement { get; set; }
    public double YmyLSensitivity { get; set; }
    public double FreshnessRequirement { get; set; }
    public List<string> EvidenceTypes { get; set; } = new List<string>();
    public double SemanticSimilarityToOtherPages { get; set; }
    public double SourceDensity { get; internal set; }
}
public sealed class AiQuestionAnalysis
{
    public List<QuestionClassification> Questions { get; set; } = new List<QuestionClassification>();
}
public sealed class QuestionClassification
{
    public string Question { get; set; } = "";
    public bool Answered { get; set; }
    public double IntentRelevance { get; set; }
    public double DemandProxy { get; set; }
    public double Uniqueness { get; set; }
}
