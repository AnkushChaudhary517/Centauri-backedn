using System;
using CentauriSeo.Core.Models.Scoring;

namespace CentauriSeo.Application.Scoring;

public static class RelevanceScorer
{
    // Relevance = Intent(0..20) + Section(0..10) + OriginalInfo(0..10) + Keyword(0..10)
    public static double Score(Level2Scores l2)
    {
        return Math.Clamp(
            l2.IntentScore + l2.SectionScore + l2.OriginalInfoScore + l2.KeywordScore,
            0.0, 40.0 // theoretical max 40 (10+10+10+10)
        );
    }
}
