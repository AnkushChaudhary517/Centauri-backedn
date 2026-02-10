using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Enums;
using System.Collections.Generic;
using System.Linq;
using CentauriSeo.Core.Models.Utilities;

namespace CentauriSeo.Application.Scoring;

public static class AuthorityScorer
{
    // Returns 0..10
    public static double Score(IEnumerable<ValidatedSentence> sentences)
    {
        var list = sentences.ToList();
        if (!list.Any()) return 0.0;

        double totalAuthority = 0.0;

        foreach (var s in list)
        {
            double functional = FunctionalTypeScore(s.FunctionalType);
            double structure = StructureScore(s.Structure);
            double informative = InformativeTypeScore(s.InformativeType);
            double voice = VoiceScore(s.Voice);

            double sentenceAuthority = (functional * 0.2) + (structure * 0.2) + (informative * 0.4) + (voice * 0.2);
            totalAuthority += sentenceAuthority;
        }

        double A_percent = ((double)totalAuthority / list.Count) * 10.0;
        return Math.Clamp(A_percent, 0.0, 10.0);
    }

    private static double FunctionalTypeScore(FunctionalType f)
    {
        switch(f)
        {
            case FunctionalType.Declarative:
                return 1.0;
            case FunctionalType.Interrogative:
                return 0.0;
            case FunctionalType.Exclamatory:
                return 0.5;
            case FunctionalType.Imperative:
                return 1.0;
            default:
                return 0.5;
        }
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
            InformativeType.Question => 0.0,
            _ => 0.5
        };
    }

    private static double VoiceScore(VoiceType v)
    {
        return v == VoiceType.Passive || v == VoiceType.Both ? 0.5 : 1.0;
    }
}
