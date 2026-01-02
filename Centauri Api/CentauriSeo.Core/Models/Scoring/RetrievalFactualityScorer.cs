using System;
using CentauriSeo.Core.Models.Scoring;

namespace CentauriSeo.Application.Scoring;

public static class RetrievalFactualityScorer
{
    // Placeholder: keep the existing pipeline field; returns a value suitable to be composed later.
    // The document expects three components (ABD, FI, EA). For now, rely on existing computed properties
    // or keep as 0 if not implemented. We'll use a simple proxy: use OriginalInfo + Credibility scaled.
    public static double Score(Level2Scores l2)
    {
        // Create a proxy aggregate 0..10
        double abd = Math.Min(10.0, l2.OriginalInfoScore); // proxy
        double fi = Math.Min(10.0, l2.CredibilityScore); // proxy
        double ea = Math.Min(10.0, l2.AuthorityScore); // proxy

        // Combine as weighted sum similar to doc: ABD*3, FI*2.5, EA*1.5 -> but we must keep final scale reasonable.
        double combined = (abd * 3.0 + fi * 2.5 + ea * 1.5) / 6.999999; // normalize back to ~0..10
        return Math.Clamp(combined, 0.0, 10.0);
    }
}
