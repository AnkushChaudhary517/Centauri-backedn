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
        return (3*l2.AnswerBlockDensityScore) + (2.5*l2.FactualIsolationScore) + (1.5*l2.EntityAlignmentScore);
    }
}
