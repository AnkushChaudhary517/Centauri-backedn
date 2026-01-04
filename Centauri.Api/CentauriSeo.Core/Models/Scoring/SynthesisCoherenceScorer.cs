using System;
using CentauriSeo.Core.Models.Scoring;

namespace CentauriSeo.Application.Scoring;

public static class SynthesisCoherenceScorer
{
    // Proxy: SN (Signal-to-Noise) from OriginalInfo, TC from Simplicity; return SN+TC
    public static double Score(Level2Scores l2)
    {
        double SN = Math.Min(10.0, l2.OriginalInfoScore); // proxy
        double TC = Math.Min(10.0, l2.SimplicityScore * 3.0); // scale simplicity into 0..10
        return Math.Clamp(SN + TC, 0.0, 20.0);
    }
}
