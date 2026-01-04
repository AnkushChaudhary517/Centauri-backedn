using CentauriSeo.Core.Models.Scoring;

namespace CentauriSeo.Application.Scoring;

public static class AiIndexingScorer
{
    // Per document: AI Indexing Score = Retrieval & Factuality + Synthesis & Coherence
    public static double Score(Level3Scores l3)
    {
        // Assume l3.*Score values are already on reasonable scales.
        return Math.Clamp(
            l3.RetrievalFactualityScore + l3.SynthesisCoherenceScore,
            0.0, 100.0);
    }
}
