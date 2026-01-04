using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace CentauriSeo.Application.Scoring;

public static class GrammarScorer
{
    // Returns 0..3.33
    public static double Score(IEnumerable<Level1Sentence> sentences)
    {
        var list = sentences.ToList();
        if (!list.Any()) return 3.333333;

        double correct = list.Count(s => s.IsGrammaticallyCorrect);
        double grammarPercent = correct / (double)list.Count * 100.0;
        double baseGrammar = grammarPercent / 10.0; // 0..10
        double final = baseGrammar / 3.0; // 0..3.33
        return Math.Clamp(final, 0.0, 3.333333);
    }
}
