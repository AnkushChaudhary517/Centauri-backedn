using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Enums;
using System.Collections.Generic;
using System.Linq;
using CentauriSeo.Core.Models.Utilities;

namespace CentauriSeo.Application.Scoring;

public static class ExpertiseScorer
{
    // Returns 0..10
    public static double Score(IEnumerable<ValidatedSentence> sentences)
    {
        int totalCount = 0;
        int expertiseCount = 0;

        foreach (var s in sentences)
        {
            totalCount++;

            // Simplified check: Opinion, Prediction, Observation, Suggestion, or has Pronouns
            bool isExpertise = s.InformativeType switch
            {
                InformativeType.Opinion => true,
                InformativeType.Prediction => true,
                InformativeType.Observation => true,
                InformativeType.Suggestion => true,
                _ => s.HasPronoun // Fallback to pronoun check
            };

            if (isExpertise) expertiseCount++;
        }

        if (totalCount == 0) return 0.0;

        double score = (expertiseCount / ((double)totalCount-sentences.Count(x => x.InformativeType == InformativeType.Uncertain))) * 10; //scaled to 10

        return Math.Clamp(score, 0.0, 10.0);
    }
}
