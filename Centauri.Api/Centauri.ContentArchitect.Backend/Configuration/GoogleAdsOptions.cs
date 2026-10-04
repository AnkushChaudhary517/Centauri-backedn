namespace Centauri.ContentArchitect.Backend.Configuration;

/// <summary>
/// Configuration options for Google Ads API client using ADC from shared GCP Project.
/// Reuses the same GCP project and credentials as GeminiClient!
/// No OAuth refresh token needed - uses Application Default Credentials.
/// </summary>
public sealed class GoogleAdsOptions
{
    public bool Enabled { get; set; } = false;

    // Google Ads specific configuration only
    public string CustomerId { get; set; } = "";

    // Targeting Configuration
    public string LanguageCode { get; set; } = "en";
    public int LocationCode { get; set; } = 2840; // United States

    // SERP Configuration
    public string SerpDevice { get; set; } = "DESKTOP";
    public int SerpDepth { get; set; } = 10;

    // Caching Configuration
    public int CacheDurationMinutes { get; set; } = 1440; // 24 hours

    // Backlink Provider Configuration (optional, for extended data)
    public string? BacklinkProvider { get; set; } // "moz", "ahrefs", "semrush"
    public string? MozApiKey { get; set; }
    public string? AhrefsApiKey { get; set; }
    public string? SemrushApiKey { get; set; }
}