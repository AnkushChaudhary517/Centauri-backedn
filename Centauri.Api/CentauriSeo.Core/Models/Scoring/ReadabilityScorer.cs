using System;
using CentauriSeo.Core.Models.Scoring;

namespace CentauriSeo.Application.Scoring;

public static class ReadabilityScorer
{
    // Readability = Simplicity(0..3.33) + Grammar(0..3.33) + Variation(0..3.33) => 0..10
    public static double Score(Level2Scores l2)
    {
        return Math.Clamp(Math.Round((l2.SimplicityScore + l2.GrammarScore + l2.VariationScore)/3.0), 0.0, 10.0);
    }
}
