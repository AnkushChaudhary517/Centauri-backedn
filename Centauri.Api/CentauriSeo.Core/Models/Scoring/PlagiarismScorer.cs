using System.Linq;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;

namespace CentauriSeo.Application.Scoring;

public static class PlagiarismScorer
{
    // Per document:
    // Plagiarism Score = (U / T) * 10
    // where U = number of unique (non-copied) sentences, T = total sentences scanned.
    // Return range: 0 .. 10
    public static double Score(IEnumerable<ValidatedSentence> sentences)
    {
        var list = sentences.ToList();
        if (!list.Any())
            return 10.0; // no sentences scanned -> maximum plagiarism score (fully unique)

        int plagiarized = list.Count(s => s.IsPlagiarized);
        int total = list.Count;

        int unique = total - plagiarized;
        double score = unique / (double)total * 10.0;

        return Math.Clamp(score, 0.0, 10.0);
    }
}
