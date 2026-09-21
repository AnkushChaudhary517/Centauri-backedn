using Centauri.ContentArchitect.Backend.Models;

namespace Centauri.ContentArchitect.Backend.Services.Calculators;

public sealed class ContentGapCalculator
{
    public ContentGapsResult Calculate(IReadOnlyList<QuestionClassification> candidates, IReadOnlyList<QuestionCoverageResult> coverage)
    {
        var output = new List<ContentGapResult>();

        foreach (var c in candidates)
        {
            var cov = coverage.FirstOrDefault(x =>
                x.Question.Equals(c.Question, StringComparison.OrdinalIgnoreCase))?.Coverage ?? 0;

            var cg = 1.0 - cov;
            var score = 100.0 * (
                0.35 * c.IntentRelevance +
                0.25 * cg +
                0.20 * c.DemandProxy +
                0.20 * c.Uniqueness);

            output.Add(new ContentGapResult
            {
                Question = c.Question,
                IntentRelevance = c.IntentRelevance,
                DemandProxy = c.DemandProxy,
                CompetitorGap = cg,
                Uniqueness = c.Uniqueness,
                Score = score,
                Classification = score >= 75 ? "Strong addition" :
                                 score >= 50 ? "Useful addition" :
                                 score >= 25 ? "Optional" : "Probably unnecessary"
            });
        }

        var rankedGaps = output.OrderByDescending(x => x.Score).ToList();
        return new ContentGapsResult
        {
            Gaps = rankedGaps,
            TopQuestions = rankedGaps.Take(10).ToList()
        };
    }
}
