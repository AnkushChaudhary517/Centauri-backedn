using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models
{
    // --- Sitemap Models ---

    public class SitemapRequest
    {
        public string Url { get; set; } = string.Empty;
    }

    public class DiscoveredPage
    {
        public string Url { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    public class SitemapResult
    {
        public string Domain { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public List<DiscoveredPage> Pages { get; set; } = new();
    }


    // --- Pipeline Models ---

    public class KeywordAnalysisRequest
    {
        public string PrimaryKeyword { get; set; } = string.Empty;
        public string TargetUrl { get; set; } = string.Empty;
        public string TargetRegion { get; set; } = "US";
    }


    public class KeywordAnalysisResult
    {
        public string PrimaryKeyword { get; set; } = string.Empty;

        public string SearchIntent { get; set; } = string.Empty;

        public List<DiscoveredPage> ReconciledPages { get; set; } = new();

        public KeywordAnalysisResponse RawAiAnalysis { get; set; } = new();
    }


    public class OutlineRequest
    {
        public KeywordAnalysisResult AnalysisData { get; set; } = new();

        public string TargetAudienceNotes { get; set; } = string.Empty;

        public List<DiscoveredPage> SelectedTargetPages { get; set; } = new();
    }


    public class OutlineResult
    {
        public string Title { get; set; } = string.Empty;

        public List<OutlineSection> Sections { get; set; } = new();
    }


    public class OutlineSection
    {
        public string Heading { get; set; } = string.Empty;

        public string Level { get; set; } = "H2";

        public List<string> BulletPoints { get; set; } = new();

        public string? SuggestedInternalLinkUrl { get; set; }

        public string? SuggestedAnchorText { get; set; }
    }


    public class ArticleRequest
    {
        public OutlineResult Outline { get; set; } = new();

        public string PrimaryKeyword { get; set; } = string.Empty;

        public List<string> LsiKeywords { get; set; } = new();

        public List<DiscoveredPage> InterlinkingMapping { get; set; } = new();
    }


    public class ArticleResult
    {
        public string MetaTitle { get; set; } = string.Empty;

        public string MetaDescription { get; set; } = string.Empty;

        public string ContentMarkdown { get; set; } = string.Empty;

        public string FaqSchemaJsonLd { get; set; } = string.Empty;
    }

    // --- Gemini Keyword Analysis Response Models ---

    public class KeywordAnalysisResponse
    {
        [JsonPropertyName("keyword")]
        public string Keyword { get; set; } = string.Empty;

        [JsonPropertyName("targetRegion")]
        public string TargetRegion { get; set; } = string.Empty;

        [JsonPropertyName("analysis")]
        public Analysis Analysis { get; set; } = new();
    }


    public class Analysis
    {
        [JsonPropertyName("searchIntent")]
        public List<SearchIntent> SearchIntent { get; set; } = new();


        [JsonPropertyName("monthlySearchVolume")]
        public MonthlySearchVolume MonthlySearchVolume { get; set; } = new();


        [JsonPropertyName("averageCpc")]
        public AverageCpc AverageCpc { get; set; } = new();


        [JsonPropertyName("keywordDifficulty")]
        public KeywordDifficulty KeywordDifficulty { get; set; } = new();


        [JsonPropertyName("trafficPotential")]
        public TrafficPotential TrafficPotential { get; set; } = new();


        [JsonPropertyName("keywordClusters")]
        public List<KeywordCluster> KeywordClusters { get; set; } = new();


        [JsonPropertyName("rankingTargets")]
        public List<RankingTarget> RankingTargets { get; set; } = new();


        [JsonPropertyName("contentGapAnalysis")]
        public ContentGapAnalysis ContentGapAnalysis { get; set; } = new();


        [JsonPropertyName("indexabilityMetrics")]
        public IndexabilityMetrics IndexabilityMetrics { get; set; } = new();
    }


    public class SearchIntent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;


        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;


        [JsonPropertyName("priority")]
        public int Priority { get; set; }
    }


    public class MonthlySearchVolume
    {
        [JsonPropertyName("value")]
        public int Value { get; set; }


        [JsonPropertyName("confidence")]
        public string Confidence { get; set; } = string.Empty;


        [JsonPropertyName("sourceNote")]
        public string SourceNote { get; set; } = string.Empty;
    }


    public class AverageCpc
    {
        [JsonPropertyName("currency")]
        public string Currency { get; set; } = string.Empty;


        [JsonPropertyName("value")]
        public decimal Value { get; set; }


        [JsonPropertyName("confidence")]
        public string Confidence { get; set; } = string.Empty;


        [JsonPropertyName("sourceNote")]
        public string SourceNote { get; set; } = string.Empty;
    }


    public class KeywordDifficulty
    {
        [JsonPropertyName("score")]
        public int Score { get; set; }


        [JsonPropertyName("level")]
        public string Level { get; set; } = string.Empty;


        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }


    public class TrafficPotential
    {
        [JsonPropertyName("monthlyClicks")]
        public int MonthlyClicks { get; set; }


        [JsonPropertyName("assumptions")]
        public string Assumptions { get; set; } = string.Empty;
    }


    public class KeywordCluster
    {
        [JsonPropertyName("clusterName")]
        public string ClusterName { get; set; } = string.Empty;


        [JsonPropertyName("intent")]
        public string Intent { get; set; } = string.Empty;


        [JsonPropertyName("keywords")]
        public List<string> Keywords { get; set; } = new();
    }


    public class RankingTarget
    {
        [JsonPropertyName("keyword")]
        public string Keyword { get; set; } = string.Empty;


        [JsonPropertyName("intent")]
        public string Intent { get; set; } = string.Empty;


        [JsonPropertyName("difficulty")]
        public int Difficulty { get; set; }
    }


    public class ContentGapAnalysis
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;


        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;


        [JsonPropertyName("missingTopics")]
        public List<string> MissingTopics { get; set; } = new();
    }


    public class IndexabilityMetrics
    {
        [JsonPropertyName("topicalRelevance")]
        public int TopicalRelevance { get; set; }


        [JsonPropertyName("authority")]
        public int Authority { get; set; }


        [JsonPropertyName("internalLinking")]
        public int InternalLinking { get; set; }


        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }
}