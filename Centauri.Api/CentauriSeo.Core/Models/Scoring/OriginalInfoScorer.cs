using CentauriSeo.Core.Models.Enums;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace CentauriSeo.Application.Scoring;

public static class OriginalInfoScorer
{
    // Returns 0..10 per document: OriginalInfo%/10
    public static double Score(IEnumerable<Level1Sentence> sentences)
    {
        var list = sentences.ToList();
        if (!list.Any()) return 0.0;

        int unique = list.Count(s => s.InfoQuality == InfoQuality.Unique);
        int lessKnown = list.Count(s => s.InfoQuality == InfoQuality.PartiallyKnown);

        double percent = (unique + lessKnown) / (double)list.Count * 100.0;
        double score = percent / 10.0;
        return Math.Clamp(score, 0.0, 10.0);
    }
}
