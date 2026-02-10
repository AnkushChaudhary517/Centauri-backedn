using CentauriSeo.Core.Models.Enums;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace CentauriSeo.Application.Scoring;

public static class SimplicityScorer
{
    // Returns 0..3.33 per document
    public static double Score(IEnumerable<ValidatedSentence> sentences)
    {
        var list = sentences.ToList();
        if (!list.Any()) return 3.333333;

        int complexCount = list.Count(s => s.Structure == SentenceStructure.Complex ||
                                           s.Structure == SentenceStructure.CompoundComplex);
        double complexityPercent = complexCount / (double)list.Count * 10.0;

        double complexityScore = complexityPercent; // 0..10
        double baseSimplicity = 10.0 - complexityScore;
        return Math.Clamp(baseSimplicity, 0.0,10.0);  //out of 10
    }
}
