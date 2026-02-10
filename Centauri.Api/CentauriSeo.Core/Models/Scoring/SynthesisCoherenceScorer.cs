using System;
using CentauriSeo.Core.Models.Scoring;

namespace CentauriSeo.Application.Scoring;

public static class SynthesisCoherenceScorer
{
    // Proxy: SN (Signal-to-Noise) from OriginalInfo, TC from Simplicity; return SN+TC
    public static double Score(Level2Scores l2)
    {
        return (l2.SignalToNoiseScore*2) + l2.TechnicalClarityScore; //Total max = 30
    }
}
