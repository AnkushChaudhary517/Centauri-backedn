using CentauriSeo.Core.Models.Scoring;

namespace CentauriSeo.Application.Scoring;

public static class Level4Engine
{
    public static Level4Scores Compute(
        Level2Scores l2,
        Level3Scores l3)
    {
        var aiIndex = AiIndexingScorer.Score(l3);

        return new Level4Scores
        {
            AiIndexingScore = aiIndex,
            CentauriSeoScore = CentauriSeoScorer.Score(
                l2,
                l3,
                new Level4Scores { AiIndexingScore = aiIndex })
        };
    }
}
