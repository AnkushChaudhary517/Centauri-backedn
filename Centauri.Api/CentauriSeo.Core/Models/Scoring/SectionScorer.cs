using CentauriSeo.Core.Models.Outputs;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CentauriSeo.Application.Scoring;

public static class SectionScorer
{
    // Returns 0..10 (approximation when SERP-derived required subtopics are not available)
    //public static double Score(IEnumerable<ValidatedSentence> sentences, string? primaryKeyword)
    //{
    //    // Proxy: count distinct informative types as a lightweight subtopic coverage proxy.
    //    // RS_total (expected required subtopics) approximated as 6.
    //    const double RS_total = 6.0;
    //    var typesCovered = sentences.Select(s => s.InformativeType).Distinct().Count();
    //    double RS_covered = Math.Min(typesCovered, RS_total);

    //    // No original subtopic detection (OG = 0)
    //    double OG = 0.0;

    //    double sectionScore = (RS_covered / RS_total) * 10.0 * (1.0 + OG / RS_total);
    //    return Math.Clamp(sectionScore, 0.0, 10.0);
    //}

    public static double Calculate(
            List<CompetitorSectionScoreResponse> competitors,
            List<string> yourHeadings)
    {
        // Normalize competitor headings
        var competitorMap = BuildSubtopicFrequencyMap(competitors);

        // Required Subtopics = appear in >= 3 competitors
        var requiredSubtopics = competitorMap
            .Where(x => x.Value.Count >= 3)
            .Select(x => x.Key)
            .ToList();

        int RS_total = requiredSubtopics.Count;

        // Normalize your headings
        var normalizedYourHeadings = yourHeadings
            .Select(Normalize)
            .ToHashSet();

        // RS_covered
        var covered = requiredSubtopics
    .Where(rs =>
        normalizedYourHeadings.Any(h =>
            Similarity(h, rs) >= 0.9
        )
    )
    .ToList();

        int RS_covered = covered.Count;

        // Missing required
        var missingRequired = requiredSubtopics
            .Except(covered)
            .ToList();

        // OG = your headings not in competitors
        var originalSubtopics = normalizedYourHeadings
            .Where(h => !competitorMap.ContainsKey(h))
            .ToList();

        int OG = originalSubtopics.Count;

        // Score formula
        double score = RS_total == 0
            ? 0
            : (double)RS_covered / RS_total * 10 * (1 + (double)OG / RS_total);

        //return new SectionScoreResult
        //{
        //    RS_Total = RS_total,
        //    RS_Covered = RS_covered,
        //    OG = OG,
        //    SectionScore = Math.Round(score, 2),
        //    MissingRequiredSubtopics = missingRequired,
        //    OriginalSubtopics = originalSubtopics
        //};
        return Math.Round(score, 2);
    }

    // ----------- HELPERS -----------
    public static double Similarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
            return 0;

        int distance = LevenshteinDistance(s1, s2);
        int maxLen = Math.Max(s1.Length, s2.Length);

        return 1.0 - (double)distance / maxLen;
    }
    private static int LevenshteinDistance(string s, string t)
    {
        var dp = new int[s.Length + 1, t.Length + 1];

        for (int i = 0; i <= s.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= t.Length; j++) dp[0, j] = j;

        for (int i = 1; i <= s.Length; i++)
        {
            for (int j = 1; j <= t.Length; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;

                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost
                );
            }
        }

        return dp[s.Length, t.Length];
    }

    private static Dictionary<string, HashSet<string>> BuildSubtopicFrequencyMap(
        List<CompetitorSectionScoreResponse> competitors)
    {
        var map = new Dictionary<string, HashSet<string>>();

        foreach (var competitor in competitors)
        {
            foreach (var heading in competitor.Headings)
            {
                var key = Normalize(heading);

                if (!map.ContainsKey(key))
                    map[key] = new HashSet<string>();

                map[key].Add(competitor.Url);
            }
        }

        return map;
    }

    private static string Normalize(string text)
    {
        text = text.ToLowerInvariant();
        text = Regex.Replace(text, @"\d+", "");
        text = Regex.Replace(text, @"[^\w\s]", "");
        text = Regex.Replace(text,
            @"\b(how|what|why|guide|overview|explained)\b", "");
        text = Regex.Replace(text, @"\s+", " ");

        // Synonym normalization
        text = text.Replace("pricing", "cost");
        text = text.Replace("price", "cost");
        text = text.Replace("fees", "cost");
        text = Regex.Replace(
        text,
        @"^\s*(
            (\(?\d+[\.\)])|        # 1. 1) (1)
            (\(?[ivxlcdm]+[\.\)])| # i. ii) iv.
            (\(?[a-z][\.\)])|      # a. b)
            [-•–—]                 # bullets
        )\s*",
        "",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace
    );

        text = Regex.Replace(text, @"^\s*[ivxlcdm]+\s+", "", RegexOptions.IgnoreCase);

        return text.Trim();
    }
}
