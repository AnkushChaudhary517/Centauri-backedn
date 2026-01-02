using System;
using CentauriSeo.Core.Models.Scoring;

namespace CentauriSeo.Application.Scoring;

public static class EeatScorer
{
    // EEAT = Expertise(0..20) + Credibility(0..10) + Authority(0..10) => 0..40
    public static double Score(Level2Scores l2)
    {
        return Math.Clamp(l2.ExpertiseScore + l2.CredibilityScore + l2.AuthorityScore, 0.0, 40.0);
    }
}
