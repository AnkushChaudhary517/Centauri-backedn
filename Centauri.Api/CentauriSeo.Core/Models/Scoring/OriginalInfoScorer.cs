using CentauriSeo.Core.Models.Enums;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace CentauriSeo.Application.Scoring;

public static class OriginalInfoScorer
{
    // Returns 0..10 per document: OriginalInfo%/10
    public static double Score(IEnumerable<ValidatedSentence> sentences)
    {
        var list = sentences.ToList();
        if (!list.Any()) return 0.0;

        int unique = list.Count(s => s.InfoQuality == InfoQuality.Unique);
        int lessKnown = list.Count(s => s.InfoQuality == InfoQuality.PartiallyKnown);

        double percent = (unique + lessKnown) / (double)list.Count;
        double score = percent*10; //adjusted to scale of 0..10
        return Math.Clamp(score, 0.0, 10.0);
    }
}
