using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Enums;
using System.Collections.Generic;
using System.Linq;
using CentauriSeo.Core.Models.Utilities;

namespace CentauriSeo.Application.Scoring;

public static class AuthorityScorer
{
    // Returns 0..10
    public static double Score(IEnumerable<Level1Sentence> sentences)
    {
        var list = sentences.ToList();
        if (!list.Any()) return 0.0;

        double totalAuthority = 0.0;

        foreach (var s in list)
        {
            double functional = FunctionalTypeScore(s.Text);
            double structure = StructureScore(s.Structure);
            double informative = InformativeTypeScore(s.InformativeType);
            double voice = VoiceScore(s.Voice);

            double sentenceAuthority = functional * structure * informative * voice;
            totalAuthority += sentenceAuthority;
        }

        double A_percent = totalAuthority / list.Count * 100.0;
        return Math.Clamp(A_percent / 10.0, 0.0, 10.0);
    }

    private static double FunctionalTypeScore(string text)
    {
        text = (text ?? "").Trim();
        if (text.EndsWith("?")) return 0.0; // interrogative: 0
        if (text.EndsWith("!")) return 0.5; // exclamatory
        // Imperative detection basic heuristic
        var firstWord = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        if (!string.IsNullOrEmpty(firstWord) && (firstWord == "please" || firstWord == "click" || firstWord == "install" || firstWord == "enable"))
            return 1.0; // imperative -> 1
        return 1.0; // declarative default
    }

    private static double StructureScore(SentenceStructure st)
    {
        return st switch
        {
            SentenceStructure.CompoundComplex => 0.5,
            SentenceStructure.Fragment => 0.0,
            _ => 1.0
        };
    }

    private static double InformativeTypeScore(InformativeType t)
    {
        return t switch
        {
            InformativeType.Fact => 1.0,
            InformativeType.Observation => 1.0,
            InformativeType.Definition => 1.0,
            InformativeType.Statistic => 1.0,
            InformativeType.Claim => 1.0,
            InformativeType.Suggestion => 1.0,
            InformativeType.Opinion => 1.0,
            InformativeType.Prediction => 1.0,
            _ => 0.5
        };
    }

    private static double VoiceScore(VoiceType v)
    {
        return v == VoiceType.Passive ? 0.5 : 1.0;
    }
}
