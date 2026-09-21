namespace Centauri.ContentArchitect.Backend.Configuration;

public sealed class DataForSeoOptions
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "https://api.dataforseo.com";
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string LanguageCode { get; set; } = "en";
    public int LocationCode { get; set; } = 2840;
    public string SerpType { get; set; } = "google";
    public string SerpDevice { get; set; } = "desktop";
    public string SerpOs { get; set; } = "windows";
    public int CacheDurationMinutes { get; set; } = 15;
}

public sealed class GeminiOptions
{
    public bool Enabled { get; set; } = true;
}

public sealed class SearchConsoleOptions
{
    public bool Enabled { get; set; } = true;
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string SiteUrl { get; set; } = "";
}

public sealed class AnalysisOptions
{
    public int CacheDurationMinutes { get; set; } = 30;
    public int TopResultCount { get; set; } = 10;
    public double QuestionCoverageCoreThreshold { get; set; } = 0.60;
    public double QuestionCoverageCommonThreshold { get; set; } = 0.30;
    public double GapStrongThreshold { get; set; } = 75;
    public double GapUsefulThreshold { get; set; } = 50;
    public double GapOptionalThreshold { get; set; } = 25;
    public double AddressableDemandDeduplicationSimilarity { get; set; } = 0.90;
    public double BacklinkBenchmark { get; set; } = 1000;
    public double DefaultIndexabilityFactor { get; set; } = 0.50;
    // PublicEstimate works for any public site. SearchConsole requires the user to authorize access to the target property.
    public string IndexabilityMode { get; set; } = "PublicEstimate";
    public int[] DefaultProjectedPositions { get; set; } = new int[] { 12, 8, 5 };
    public Dictionary<string, double> CtrByPosition { get; set; } = new Dictionary<string, double>();
    public SerpClickabilityOptions SerpClickability { get; set; } = new SerpClickabilityOptions();
    public SamplingOptions Sampling { get; set; } = new SamplingOptions();
}
public sealed class SerpClickabilityOptions
{
    public double Normal { get; set; } = 1.0;
    public double HeavyPaaVideo { get; set; } = 0.90;
    public double FeaturedSnippet { get; set; } = 0.85;
    public double AiOverview { get; set; } = 0.70;
    public double HeavyAdsAndAiOverview { get; set; } = 0.55;
}
public sealed class SamplingOptions
{
    public int MaxSitemapUrls { get; set; } = 100;
    public int MaxGscInspectionsPerRun { get; set; } = 100;
    public int InspectionLookbackDays { get; set; } = 30;
}
