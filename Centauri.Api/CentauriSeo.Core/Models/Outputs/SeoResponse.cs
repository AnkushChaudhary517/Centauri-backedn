using System;
using System.Collections.Generic;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Scoring;

namespace CentauriSeo.Core.Models.Output;

// Full response model aligned to the document's mandatory JSON schema.
// Kept minimal implementation but includes all required top-level sections.
// Consumers can still read Level2Scores/Level3Scores/Level4Scores for backwards compatibility.
public class SeoResponse
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public string Status { get; set; } = "partial";

    public InputIntegrity InputIntegrity { get; set; } = new();

    public Level1Output Level1 { get; set; } = new();

    public Level2Output Level2 { get; set; } = new();

    public Level3Output Level3 { get; set; } = new();

    public Level4Output Level4 { get; set; } = new();

    public FinalScores FinalScores { get; set; } = new();

    public Diagnostics Diagnostics { get; set; } = new();

    public List<Recommendation> Recommendations { get; set; } = new();

    // Backwards-compatible properties
    public Level2Scores Level2Scores { get; set; } = new();
    public Level3Scores Level3Scores { get; set; } = new();
    public Level4Scores Level4Scores { get; set; } = new();
    public double SeoScore { get; set; }
    public List<string> RecommendationsLegacy { get; set; } = new();
}

public class InputIntegrity
{
    public string Status { get; set; } = "partial";
    public ReceivedInputs Received { get; set; } = new();
    public List<string> MissingInputs { get; set; } = new();
    public List<string> InvalidInputs { get; set; } = new();
    public Dictionary<string, bool> DefaultsApplied { get; set; } = new();
    public List<string> SkippedChecks { get; set; } = new();
    public List<string> NotEvaluatedMetrics { get; set; } = new();
    public List<string> Messages { get; set; } = new();
}

public class ReceivedInputs
{
    public bool ArticlePresent { get; set; }
    public bool PrimaryKeywordPresent { get; set; }
    public bool SecondaryKeywordsPresent { get; set; }
    public bool MetaTitlePresent { get; set; }
    public bool MetaDescriptionPresent { get; set; }
    public bool UrlPresent { get; set; }
}

public class Level1Output
{
    public Level1Summary Summary { get; set; } = new();
    public bool SentenceMapIncluded { get; set; } = false;
    public List<SentenceMapEntry> SentenceMap { get; set; } = new();
}

public class Level1Summary
{
    public int SentenceCount { get; set; }
    public Dictionary<string, int> StructureDistribution { get; set; } = new();
    public Dictionary<string, int> InformativeTypeDistribution { get; set; } = new();
    public Dictionary<string, int> CitationDistribution { get; set; } = new();
    public Dictionary<string, int> GrammarDistribution { get; set; } = new();
}

public class SentenceMapEntry
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public FinalTags FinalTags { get; set; } = new();
}

public class FinalTags
{
    public string InformativeType { get; set; } = "";
    public string Citation { get; set; } = "";
    public string Structure { get; set; } = "";
    public string Voice { get; set; } = "";
    public string Grammar { get; set; } = "";
}

public class Level2Output
{
    public ScoreEntry OriginalInfoScore { get; set; } = new();
    public ScoreEntry ExpertiseScore { get; set; } = new();
    public ScoreEntry CredibilityScore { get; set; } = new();
    public ScoreEntry AuthorityScore { get; set; } = new();
    public ScoreEntry IntentScore { get; set; } = new();
    public ScoreEntry SectionScore { get; set; } = new();
    public ScoreEntry KeywordScore { get; set; } = new();
    public ScoreEntry SimplicityScore { get; set; } = new();
    public ScoreEntry GrammarScore { get; set; } = new();
    public ScoreEntry VariationScore { get; set; } = new();
    public ScoreEntry PlagiarismScore { get; set; } = new();
}

public class ScoreEntry
{
    public double? Value { get; set; }
    public string Status { get; set; } = "not_evaluated"; // "computed" | "not_evaluated"
    public string Reason { get; set; } = "";
    public List<string> Components { get; set; } = new();
}

public class Level3Output
{
    public CompositeScore RelevanceScore { get; set; } = new();
    public CompositeScore EeatScore { get; set; } = new();
    public CompositeScore ReadabilityScore { get; set; } = new();
    public ScoreEntry RetrievalFactuality { get; set; } = new();
    public ScoreEntry SynthesisCoherence { get; set; } = new();
}

public class CompositeScore
{
    public double? Value { get; set; }
    public string Status { get; set; } = "not_evaluated";
    public List<string> Components { get; set; } = new();
}

public class Level4Output
{
    public ScoreEntry AiIndexingScore { get; set; } = new();
    public ScoreEntry SeoScore { get; set; } = new();
}

public class FinalScores
{
    public UserVisibleFinal UserVisible { get; set; } = new();
    public object Internal { get; set; } = new { };
}

public class UserVisibleFinal
{
    public double? SeoScore { get; set; }
    public double? RelevanceScore { get; set; }
    public double? EeatScore { get; set; }
    public double? ReadabilityScore { get; set; }
    public double? AiIndexingScore { get; set; }
}

public class Diagnostics
{
    public List<TopIssue> TopIssues { get; set; } = new();
    public List<string> SkippedDueToMissingInputs { get; set; } = new();
}

public class TopIssue
{
    public string Issue { get; set; } = "";
    public string Severity { get; set; } = "";
    public List<string> Evidence { get; set; } = new();
    public List<string> Impact { get; set; } = new();
}

public class Recommendation
{
    public string Issue { get; set; } = "";
    public string WhatToChange { get; set; } = "";
    public ExamplePair Examples { get; set; } = new();
    public List<string> Improves { get; set; } = new();
}

public class ExamplePair
{
    public string Bad { get; set; } = "";
    public string Good { get; set; } = "";
}
