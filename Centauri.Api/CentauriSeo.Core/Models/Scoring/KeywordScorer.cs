using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Outputs;

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
    public static async Task<double> CalculateKeywordScore(string primaryKeyword, List<string> secondaryKeywords, List<CentauriSeo.Core.Models.Outputs.Variant2> variants, ContentData content)
    {
        // 1. Safety check
        if (string.IsNullOrEmpty(primaryKeyword) || content == null) return 0.0;

        variants.Add(new Variant2()
        {
            Text = primaryKeyword,
            VariantType=Variant2Type.Exact
        });
        // 2. Ensure variants are distinct and lowercase
        //var uniqueVariants = variants.Select(v => v.Text.ToLower()).ToList();
        //if (!uniqueVariants.Contains(primaryKeyword.ToLower()))
        //{
        //    uniqueVariants.Add(primaryKeyword.ToLower());
        //}

        // 3. P-Score (Max 5.0) -> Normalized to 10.0
        double pScore = CalculatePScore(variants, secondaryKeywords, content);

        // 4. F-Score (Max 10.0)
        double fScore = CalculateFScore(variants, secondaryKeywords, content.RawBodyText);

        // 5. Final Weighted Calculation (0.6 + 0.4 = 1.0 weight)
        double finalKs = (pScore * 0.6) + (fScore * 0.4);

        // 6. Round and Cap
        return Math.Min(10.0, Math.Round(finalKs, 2));
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

    private static double CalculatePScore(List<Variant2> variants, List<string> skList, ContentData content)
    {
        var h1Res = ContainsAny(content.H1, variants);
        var titalRes = ContainsAny(content.MetaTitle, variants);
        var urlRes = ContainsAny(content.UrlSlug, variants);
        var descRes = ContainsAny(content.MetaDescription, variants);
        double f_h1 = 0;
        double f_title = 0;
        double f_url = 0;//TODO : similarity
        double f_desc = 0;

        if (h1Res != null)
        {
            f_h1 = GetScore(h1Res.VariantType);
        }

        if (titalRes != null)
        {
            f_title = GetScore(titalRes.VariantType);
        }

        if (urlRes != null)
        {
            f_url = GetScore(urlRes.VariantType);
        }

        if (descRes != null)
        {
            f_desc = GetScore(descRes.VariantType);
        }



        //double h2h3MatchRate = CalculateHeaderMatchRate(content.HeadersH2H3, skList);
        //int f_h2_h3 = h2h3MatchRate >= 0.5 ? 1 : 0;

        return (f_h1 * W_H1) + (f_title * W_Title) + (f_url * W_URL) + (f_desc * W_Desc);
    }

    public static double GetScore(Variant2Type variant2Type)
    {
        switch (variant2Type)
        {
            case Variant2Type.Exact:
                return 1;
            case Variant2Type.Lexical:
                return 0.95;
            case Variant2Type.Semantic:
                return 0.90;
            case Variant2Type.SearchDerived:
                return 0.85;
            case Variant2Type.Morphological:
                return 0.80;
        }
        return 0.0;
    }
    private static double CalculateFScore(List<Variant2> variants, List<string> skList, string bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText)) return 0;

        int W_total = bodyText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count();
        double C_PTK = (double)CountOccurrences(bodyText, variants);

        var D_PTK_Target = (C_PTK / (double)W_total)*100;

        // Score_PTK (Primary): Perfect 10 if 0.5-1.5%. If > 1.5%, start Over-optimization penalty.
        double scorePTK = 0;
        if (D_PTK_Target >= 0.5 && D_PTK_Target <= 1.5) scorePTK = 10;
        else if (D_PTK_Target > 1.5) scorePTK = 7; // Keyword stuffing penalty
        else if (D_PTK_Target > 0) scorePTK = 5;

        return scorePTK;
    }
    // Helper: Case-insensitive search for any string in a list
    private static Variant2 ContainsAny(string text, List<Variant2> items)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return items.Where(item => text.Contains(item?.Text, StringComparison.OrdinalIgnoreCase))?.Select(x => x)?.FirstOrDefault();
    }


    // Helper: Counts total occurrences of a pool of keywords
    private static int CountOccurrences(string text, List<Variant2> pool)
    {
        int count = 0;
        foreach (var variant in pool)
        {
            var c = Regex.Matches(text, Regex.Escape(variant.Text), RegexOptions.IgnoreCase).Count;
            if(c>0)
            {
                
                var countOfWordInPresentKeyword = variant.Text.Split(' ').Count();
                count += countOfWordInPresentKeyword;
            }

        }
        return count;
    }

    //private static double CalculateHeaderMatchRate(List<string> headers, List<string> skList)
    //{
    //    if (headers.Count == 0) return 0;
    //    int matches = headers.Count(h => ContainsAny(h, skList));
    //    return (double)matches / headers.Count;
    //}
}
