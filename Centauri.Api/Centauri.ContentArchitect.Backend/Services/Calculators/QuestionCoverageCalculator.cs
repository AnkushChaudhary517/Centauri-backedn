using Centauri.ContentArchitect.Backend.Models;

namespace Centauri.ContentArchitect.Backend.Services.Calculators;

public sealed class QuestionCoverageCalculator
{
    public QuestionsAnsweredResult Calculate(
        IReadOnlyList<string> questions,
        IReadOnlyList<SerpResult> top10)
    {
        var results = new List<QuestionCoverageResult>();
        foreach (var q in questions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            double numerator = 0;
            double denominator = 0;
            foreach (var page in top10.Take(10))
            {
                var weight = 1.0 / Math.Log2(page.Position + 1);
                var answered = page.Questions.Any(x =>
                    SimilarEnough(x, q)) ? 1.0 : 0.0;
                numerator += weight * answered;
                denominator += weight;
            }

            var coverage = denominator == 0 ? 0 : numerator / denominator;
            results.Add(new QuestionCoverageResult
            {
                Question = q,
                Coverage = coverage,
                Classification = coverage >= 0.60 ? "Core question" :
                                 coverage >= 0.30 ? "Common question" : "Potential gap"
            });
        }
        return new QuestionsAnsweredResult { Questions = results };
    }

    private static bool SimilarEnough(string a, string b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        return na.Contains(nb, StringComparison.OrdinalIgnoreCase) ||
               nb.Contains(na, StringComparison.OrdinalIgnoreCase) ||
               TokenJaccard(na, nb) >= 0.55;
    }

    private static string Normalize(string s)
        => string.Join(' ', s.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\r', '\n', '?', '.', ',', ':', ';' },
                   StringSplitOptions.RemoveEmptyEntries));

    private static double TokenJaccard(string a, string b)
    {
        var x = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var y = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (x.Count == 0 && y.Count == 0) return 1;
        return x.Intersect(y).Count() / (double)Math.Max(1, x.Union(y).Count());
    }
}
