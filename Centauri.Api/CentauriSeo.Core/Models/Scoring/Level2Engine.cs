using CentauriSeo.Core.Models.Input;
using CentauriSeo.Core.Models.Outputs;
using CentauriSeo.Core.Models.Scoring;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using System.Collections.Generic;

namespace CentauriSeo.Application.Scoring;

public static class Level2Engine
{
    public static Level2Scores Compute(
        SeoRequest request,
        OrchestratorResponse orchestratorResponse)
    {
        var validated = orchestratorResponse?.ValidatedSentences;
        if (validated == null)
            return new Level2Scores(); // all zeros
        // Per-document, each Level2 scorer expects the validated sentence map (tags/confidence)
        // and/or the Level1 sentence data (InfoQuality/HasCitation/Structure/etc.).
        return new Level2Scores
        {
            IntentScore = IntentScorer.Score(validated, request?.PrimaryKeyword),
            SectionScore = SectionScorer.Score(validated, request?.PrimaryKeyword),
            KeywordScore = KeywordScorer.Score(validated, request?.PrimaryKeyword, request?.SecondaryKeywords,
                                              request?.MetaTitle, request?.MetaDescription, request?.Url),

            OriginalInfoScore = OriginalInfoScorer.Score(validated),
            ExpertiseScore = ExpertiseScorer.Score(validated),
            CredibilityScore = CredibilityScorer.Score(validated),
            AuthorityScore = AuthorityScorer.Score(validated),

            SimplicityScore = SimplicityScorer.Score(validated),
            GrammarScore = GrammarScorer.Score(validated),
            VariationScore = VariationScorer.Score(validated),
            PlagiarismScore = PlagiarismScorer.Score(validated),
            //ClaritySynthesisScore = ClaritySynthesisScorer.Score(validated),
            //FactRetrievalScore = FactRetrievalScorer.Score(validated),
            AnswerBlockDensityScore = AnswerBlockDensityScorer.Score(orchestratorResponse),
            FactualIsolationScore = FactualIsolationScorer.Score(orchestratorResponse),
            EntityAlignmentScore = EntityAlignmentScorer.Score(orchestratorResponse),
            TechnicalClarityScore = TechnicalClarityScorer.Score(orchestratorResponse),
            SignalToNoiseScore = SignalToNoiseScorer.Score(orchestratorResponse)

        };
    }
}
