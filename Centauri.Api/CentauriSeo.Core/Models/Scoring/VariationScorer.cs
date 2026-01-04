using CentauriSeo.Core.Models.Enums;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace CentauriSeo.Application.Scoring;

public static class VariationScorer
{
    // Returns 0..3.33 by computing normalized Shannon entropy across 5 bins approximated from sentence structure distribution.
    public static double Score(IEnumerable<Level1Sentence> sentences)
    {
        var list = sentences.ToList();
        if (!list.Any()) return 3.333333;

        // map to five bins: simple, compound, complex, compound_complex, fragment
        var total = list.Count;
        double pSimple = list.Count(s => s.Structure == SentenceStructure.Simple) / (double)total;
        double pCompound = list.Count(s => s.Structure == SentenceStructure.Compound) / (double)total;
        double pComplex = list.Count(s => s.Structure == SentenceStructure.Complex) / (double)total;
        double pCompoundComplex = list.Count(s => s.Structure == SentenceStructure.CompoundComplex) / (double)total;
        double pFragment = list.Count(s => s.Structure == SentenceStructure.Fragment) / (double)total;

        double H = 0.0;
        foreach (var p in new[] { pSimple, pCompound, pComplex, pCompoundComplex, pFragment })
        {
            if (p > 0) H -= p * Math.Log2(p);
        }

        double Hmax = Math.Log2(5);
        double V10 = (H / Hmax) * 10.0;
        double variation = V10 / 3.0; // 0..3.33
        return Math.Clamp(variation, 0.0, 3.333333);
    }
}
