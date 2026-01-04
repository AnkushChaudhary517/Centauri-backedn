using CentauriSeo.Core.Models.Scoring;

namespace CentauriSeo.Application.Scoring;

public static class CentauriSeoScorer
{
    public static double Score(
        Level2Scores l2,
        Level3Scores l3,
        Level4Scores l4)
    {
        // Primary source per spec: l3 (Relevance + EEAT + Readability)
        double relevance = l3?.RelevanceScore ?? 0.0;
        double eeat = l3?.EeatScore ?? 0.0;
        double readability = l3?.ReadabilityScore ?? 0.0;

        // Fallbacks: if any l3 component is zero, compute from l2
        if (relevance <= 0.0 && l2 != null)
        {
            relevance = RelevanceScorer.Score(l2);
        }

        if (eeat <= 0.0 && l2 != null)
        {
            eeat = EeatScorer.Score(l2);
        }

        if (readability <= 0.0 && l2 != null)
        {
            readability = ReadabilityScorer.Score(l2);
        }

        double total = relevance + eeat + readability;

        // Final fallback: if nothing computed from l3/l2, use l4.AiIndexingScore if available
        if (total <= 0.0 && l4 != null && l4.AiIndexingScore > 0.0)
        {
            return Math.Clamp(l4.AiIndexingScore, 0.0, 100.0);
        }

        return Math.Clamp(total, 0.0, 100.0);
    }
}
