using CentauriSeo.Core.Models.Input;
using CentauriSeo.Core.Models.Scoring;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using System.Collections.Generic;

namespace CentauriSeo.Application.Scoring;

public static class Level2Engine
{
    public static Level2Scores Compute(
        SeoRequest request,
        IReadOnlyList<ValidatedSentence> validated,
        IReadOnlyList<Level1Sentence> level1)
    {
        // Per-document, each Level2 scorer expects the validated sentence map (tags/confidence)
        // and/or the Level1 sentence data (InfoQuality/HasCitation/Structure/etc.).
        return new Level2Scores
        {
            IntentScore = IntentScorer.Score(validated, request?.PrimaryKeyword),
            SectionScore = SectionScorer.Score(level1, request?.PrimaryKeyword),
            KeywordScore = KeywordScorer.Score(validated, request?.PrimaryKeyword, request?.SecondaryKeywords,
                                              request?.MetaTitle, request?.MetaDescription, request?.Url),

            OriginalInfoScore = OriginalInfoScorer.Score(level1),
            ExpertiseScore = ExpertiseScorer.Score(level1),
            CredibilityScore = CredibilityScorer.Score(level1),
            AuthorityScore = AuthorityScorer.Score(level1),

            SimplicityScore = SimplicityScorer.Score(level1),
            GrammarScore = GrammarScorer.Score(level1),
            VariationScore = VariationScorer.Score(level1),
            PlagiarismScore = PlagiarismScorer.Score(level1)
        };
    }
}
