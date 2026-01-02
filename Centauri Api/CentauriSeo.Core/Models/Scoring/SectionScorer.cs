using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace CentauriSeo.Application.Scoring;

public static class SectionScorer
{
    // Returns 0..10 (approximation when SERP-derived required subtopics are not available)
    public static double Score(IEnumerable<Level1Sentence> sentences, string? primaryKeyword)
    {
        // Proxy: count distinct informative types as a lightweight subtopic coverage proxy.
        // RS_total (expected required subtopics) approximated as 6.
        const double RS_total = 6.0;
        var typesCovered = sentences.Select(s => s.InformativeType).Distinct().Count();
        double RS_covered = Math.Min(typesCovered, RS_total);

        // No original subtopic detection (OG = 0)
        double OG = 0.0;

        double sectionScore = (RS_covered / RS_total) * 10.0 * (1.0 + OG / RS_total);
        return Math.Clamp(sectionScore, 0.0, 10.0);
    }
}
