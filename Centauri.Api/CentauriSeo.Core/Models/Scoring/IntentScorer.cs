using CentauriSeo.Core.Models.Enums;
using CentauriSeo.Core.Models.Sentences;
using System.Collections.Generic;
using System.Linq;

namespace CentauriSeo.Application.Scoring;

public static class IntentScorer
{
    // Returns 0-10
    public static double Score(IEnumerable<ValidatedSentence> sentences, string? primaryKeyword)
    {
        // In absence of SERP-derived intent distribution, approximate:
        // ratio of informational sentences (Fact/Definition/Observation) -> scale to 0..10
        var list = sentences.ToList();
        if (!list.Any()) return 0.0;

        int informational = list.Count(s =>
            s.InformativeType == InformativeType.Fact ||
            s.InformativeType == InformativeType.Definition ||
            s.InformativeType == InformativeType.Observation);

        double ratio = (informational / (double)list.Count)/10; //base 10
        return Math.Clamp(ratio * 10, 0.0, 10.0); ///0...10
    }
}
