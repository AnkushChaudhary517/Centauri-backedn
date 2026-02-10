using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace CentauriSeo.Application.Scoring;

public static class GrammarScorer
{
    // Returns 0..3.33
    public static double Score(IEnumerable<ValidatedSentence> sentences)
    {
        var list = sentences.ToList();
        if (!list.Any()) return 10.0;

        double correct = list.Count(s => s.IsGrammaticallyCorrect) + list.Count(x => !x.IsGrammaticallyCorrect && x.Text.Split(' ').Length<=2);
        double grammarPercent = correct / (double)list.Count * 10.0;
        double baseGrammar = grammarPercent; // 0..10
        return Math.Clamp(baseGrammar, 0.0, 10.0);
    }
}
