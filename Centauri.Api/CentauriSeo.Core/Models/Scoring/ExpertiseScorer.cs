using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Enums;
using System.Collections.Generic;
using System.Linq;
using CentauriSeo.Core.Models.Utilities;

namespace CentauriSeo.Application.Scoring;

public static class ExpertiseScorer
{
    // Returns 0..20
    public static double Score(IEnumerable<Level1Sentence> sentences)
    {
        var list = sentences.ToList();
        if (!list.Any()) return 0.0;

        int E = list.Count(s =>
            s.InformativeType == InformativeType.Opinion ||
            s.InformativeType == InformativeType.Prediction ||
            s.InformativeType == InformativeType.Observation ||
            (s.HasPronoun) ||
            s.InformativeType == InformativeType.Suggestion);

        double expertisePercent = E / (double)list.Count * 100.0;
        double baseScore = expertisePercent / 10.0; // 0..10
        return Math.Clamp(baseScore * 2.0, 0.0, 20.0);
    }
}
