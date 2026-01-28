using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using CentauriSeo.Core.Models.Sentences;

namespace CentauriSeo.Application.Scoring;

public static class KeywordScorer
{
    // Returns 0..10
    //public static double Score(
    //    IEnumerable<ValidatedSentence> sentences,
    //    string? primary,
    //    IEnumerable<string>? secondary,
    //    string? metaTitle = null,
    //    string? metaDescription = null,
    //    string? url = null)
    //{
    //    if (string.IsNullOrWhiteSpace(primary) && (secondary == null || !secondary.Any()))
    //        return 0.0;

    //    var text = string.Join(" ", sentences.Select(s => s.Text)).ToLowerInvariant();
    //    var totalWords = Regex.Matches(text, @"\b\w+\b").Count;
    //    totalWords = Math.Max(1, totalWords);

    //    var pk = (primary ?? "").Trim().ToLowerInvariant();
    //    var sks = (secondary ?? Enumerable.Empty<string>()).Select(s => s.Trim().ToLowerInvariant()).ToList();

    //    // Build very small variant set (exact + plural)
    //    var pkVariants = new HashSet<string> { pk };
    //    if (!string.IsNullOrEmpty(pk) && !pk.EndsWith("s")) pkVariants.Add(pk + "s");

    //    // Placement flags
    //    int F_H1 = (!string.IsNullOrEmpty(metaTitle) && pkVariants.Any(v => metaTitle!.ToLowerInvariant().Contains(v))) ? 1 : 0;
    //    int F_Title = F_H1; // reuse meta title as Title
    //    int F_URL = (!string.IsNullOrEmpty(url) && pkVariants.Any(v => url!.ToLowerInvariant().Contains(v))) ? 1 : 0;
    //    int F_H2_H3 = 0; // header detection not available -> conservative 0
    //    int F_Desc = (!string.IsNullOrEmpty(metaDescription) && pkVariants.Any(v => metaDescription!.ToLowerInvariant().Contains(v))) ? 1 : 0;

    //    double PScore = F_H1 * 3.0 + F_Title * 3.0 + F_URL * 2.0 + F_H2_H3 * 1.0 + F_Desc * 1.0;
    //    // Normalize PScore from 0..10
    //    PScore = Math.Clamp(PScore, 0.0, 10.0);

    //    // Frequency: PTK and SK density scoring
    //    int primaryCount = pkVariants.Sum(v => Regex.Matches(text, $@"\b{Regex.Escape(v)}\b").Count);
    //    int secondaryCount = sks.Sum(k => Regex.Matches(text, $@"\b{Regex.Escape(k)}\b").Count);

    //    double D_PTK_Actual = primaryCount / (double)totalWords;
    //    double D_SK_Actual = secondaryCount / (double)totalWords;

    //    // Target ranges
    //    (double low, double high) PTK_target = (0.005, 0.015); // 0.5% - 1.5%
    //    (double low, double high) SK_target = (0.015, 0.03);  // 1.5% - 3.0%

    //    double scorePTK = DensityToScore(D_PTK_Actual, PTK_target);
    //    double scoreSK = DensityToScore(D_SK_Actual, SK_target);

    //    double FScore = (scorePTK + scoreSK) / 2.0; // 0..10

    //    // Final Keyword Score
    //    double KS = (PScore * 6.0 / 10.0) + (FScore * 4.0 / 10.0);
    //    return Math.Clamp(KS, 0.0, 10.0);
    //}


    // Weights from Centauri Documentation
    private const double W_H1 = 3.0;
    private const double W_Title = 3.0;
    private const double W_URL = 2.0;
    private const double W_H2_H3 = 1.0;
    private const double W_Desc = 1.0;

    public static async Task<double> CalculateKeywordScore(string primaryKeyword, List<string> secondaryKeywords,List<string> variants, ContentData content)
    {
        variants.Add(primaryKeyword.ToLower()); // Ensure exact match is in pool

        // 2. Calculate P-Score (Placement)
        double pScore = CalculatePScore(variants, secondaryKeywords, content);

        // 3. Calculate F-Score (Frequency)
        double fScore = CalculateFScore(variants, secondaryKeywords, content.RawBodyText);

        // 4. Final Weighted Calculation (60/40 Rule)
        double finalKs = (pScore * 0.6) + (fScore * 0.4);

        //return new KeywordScoreResult
        //{
        //    P_Score = pScore,
        //    F_Score = fScore,
        //    FinalKS = Math.Round(finalKs, 2),
        //    VariantsUsed = variants
        //};
        return Math.Round(finalKs, 2);
    }
    private static double DensityToScore(double actual, (double low, double high) target)
    {
        // Returns 0..10
        if (actual >= target.low && actual <= target.high) return 10.0;

        double center = (target.low + target.high) / 2.0;
        double diff = Math.Abs(actual - center);

        // small deviation (<50% of target width) -> 5..9
        double width = (target.high - target.low);
        if (diff <= width * 0.5)
        {
            // map diff 0..(width*0.5) -> 9..5
            return 9.0 - (diff / (width * 0.5)) * 4.0;
        }

        // large deviation -> 0
        return 0.0;
    }

    private static double CalculatePScore(List<string> variants, List<string> skList, ContentData content)
    {
        int f_h1 = ContainsAny(content.H1, variants) ? 1 : 0;
        int f_title = ContainsAny(content.MetaTitle, variants) ? 1 : 0;
        int f_url = ContainsAny(content.UrlSlug, variants) ? 1 : 0;
        int f_desc = ContainsAny(content.MetaDescription, variants) || ContainsAny(content.MetaDescription, skList) ? 1 : 0;

        // H2/H3 Logic: 50% must contain SK or variations
        double h2h3MatchRate = CalculateHeaderMatchRate(content.HeadersH2H3, skList);
        int f_h2_h3 = h2h3MatchRate >= 0.5 ? 1 : 0;

        return (f_h1 * W_H1) + (f_title * W_Title) + (f_url * W_URL) + (f_h2_h3 * W_H2_H3) + (f_desc * W_Desc);
    }

    private static double CalculateFScore(List<string> variants, List<string> skList, string bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText)) return 0;

        int totalWords = bodyText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        int pkCount = CountOccurrences(bodyText, variants);
        int skCount = CountOccurrences(bodyText, skList);

        double pkDensity = (double)pkCount / totalWords * 100;
        double skDensity = (double)skCount / totalWords * 100;

        // Score_PTK: Target 0.5% - 1.5%
        double scorePTK = (pkDensity >= 0.5 && pkDensity <= 1.5) ? 10 : (pkDensity > 0 ? 5 : 0);

        // Score_SK: Target 1.5% - 3.0%
        double scoreSK = (skDensity >= 1.5 && skDensity <= 3.0) ? 10 : (skDensity > 0 ? 5 : 0);

        return (scorePTK + scoreSK) / 2.0;
    }

    // Helper: Case-insensitive search for any string in a list
    private static bool ContainsAny(string text, List<string> items)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return items.Any(item => text.Contains(item, StringComparison.OrdinalIgnoreCase));
    }

    // Helper: Counts total occurrences of a pool of keywords
    private static int CountOccurrences(string text, List<string> pool)
    {
        int count = 0;
        foreach (var variant in pool)
        {
            count += Regex.Matches(text, Regex.Escape(variant), RegexOptions.IgnoreCase).Count;
        }
        return count;
    }

    private static double CalculateHeaderMatchRate(List<string> headers, List<string> skList)
    {
        if (headers.Count == 0) return 0;
        int matches = headers.Count(h => ContainsAny(h, skList));
        return (double)matches / headers.Count;
    }
}
