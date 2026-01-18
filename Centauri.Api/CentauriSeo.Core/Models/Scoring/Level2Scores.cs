namespace CentauriSeo.Core.Models.Scoring;

public class Level2Scores
{
    public double IntentScore { get; init; }
    public double SectionScore { get; set; }
    public double KeywordScore { get; init; }
    public double OriginalInfoScore { get; init; }
    public double ExpertiseScore { get; init; }
    public double CredibilityScore { get; init; }
    public double AuthorityScore { get; set; }
    public double SimplicityScore { get; init; }
    public double GrammarScore { get; init; }
    public double VariationScore { get; init; }
    public double PlagiarismScore { get; set; }
    //public double FactRetrievalScore { get; init; }
    //public double ClaritySynthesisScore { get; init; }
    public double FactualIsolationScore { get; set; }
    public double AnswerBlockDensityScore { get; set; }
    public double EntityAlignmentScore { get; set; }
    public double SignalToNoiseScore { get; set; }
    public double TechnicalClarityScore { get; set; }
}
