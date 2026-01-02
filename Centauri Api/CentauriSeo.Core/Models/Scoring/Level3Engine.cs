using CentauriSeo.Core.Models.Scoring;

namespace CentauriSeo.Application.Scoring;

public static class Level3Engine
{
    public static Level3Scores Compute(Level2Scores l2)
    {
        return new Level3Scores
        {
            RelevanceScore = RelevanceScorer.Score(l2),
            EeatScore = EeatScorer.Score(l2),
            ReadabilityScore = ReadabilityScorer.Score(l2),
            RetrievalFactualityScore = RetrievalFactualityScorer.Score(l2),
            SynthesisCoherenceScore = SynthesisCoherenceScorer.Score(l2)
        };
    }
}
