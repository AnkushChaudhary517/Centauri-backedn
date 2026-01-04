using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Enums;
using System.Collections.Generic;
using System.Linq;
using CentauriSeo.Core.Models.Utilities;

namespace CentauriSeo.Application.Scoring;

public static class CredibilityScorer
{
    // Returns 0..10 per documented 4-level priority rule
    public static double Score(IEnumerable<Level1Sentence> sentences)
    {
        var list = sentences.ToList();
        if (!list.Any()) return 0.0;

        int C = 0;

        // First pass: if any Statistic without citation => immediate 0 (strict rule)
        if (list.Any(s => s.InformativeType == InformativeType.Statistic && !s.HasCitation))
            return 0.0;

        foreach (var s in list)
        {
            if (s.InformativeType == InformativeType.Statistic)
            {
                if (s.HasCitation) C += 1;
            }
            else if (s.InformativeType == InformativeType.Prediction)
            {
                if (s.HasCitation) C += 1;
            }
            else if (s.InformativeType == InformativeType.Claim || s.InformativeType == InformativeType.Definition)
            {
                if (s.HasCitation) C += 1;
                else if (s.InfoQuality == InfoQuality.PartiallyKnown || s.InfoQuality == InfoQuality.Unique)
                    C += 1;
            }
            else if (s.InformativeType == InformativeType.Opinion)
            {
                if (s.HasCitation) C += 1;
            }
            // others contribute 0
        }

        double credibilityPercent = C / (double)list.Count * 100.0;
        return Math.Clamp(credibilityPercent / 10.0, 0.0, 10.0);
    }
}
