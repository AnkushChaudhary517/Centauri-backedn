

using CentauriSeo.Core.Models.Enums;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Infrastructure.LlmDtos;

namespace CentauriSeo.Application.Pipeline;

public static class Phase2_ArbitrationEngine
{
    public static ValidatedSentence Arbitrate(
        Sentence sentence,
        GeminiSentenceTag localTags,
        GeminiSentenceTag g,
        ChatgptGeminiSentenceTag? aiDecision)
    {
        InformativeType finalType;
        double confidence = 0.7;

        //FunctionalType functionalType = g.FunctionalType;
        //if(p.FunctionalType != g.FunctionalType && aiDecision?.FunctionalType != null)
        //  functionalType = aiDecision.FunctionalType;

        //VoiceType voiceType = g.Voice;
        //if (p.Voice != g.Voice && aiDecision?.Voice != null)
        //    voiceType = aiDecision.Voice;

        // Rule 1: Statistic must contain number
        if ((localTags.InformativeType == InformativeType.Statistic ||
             g.InformativeType == InformativeType.Statistic)
            && sentence.Text.Any(char.IsDigit))
        {
            finalType = InformativeType.Statistic;
            confidence = 0.95;
        }
        // Rule 2: Prediction beats claim
        else if (localTags.InformativeType == InformativeType.Prediction ||
                 g.InformativeType == InformativeType.Prediction)
        {
            finalType = InformativeType.Prediction;
            confidence = 0.9;
        }
        // Rule 3: Opinion requires belief marker
        else if (localTags.InformativeType == InformativeType.Opinion ||
                  g.InformativeType == InformativeType.Opinion)
        {
            finalType = InformativeType.Opinion;
            confidence = 0.9;
        }
        // Rule 4: AI arbitration fallback
        else if (aiDecision != null)
        {
            finalType = aiDecision.InformativeType;
            confidence = aiDecision.Confidence;
        }
        else
        {
            finalType = localTags.InformativeType;
        }

        return new ValidatedSentence
        {
            Id = sentence.Id,
            Text = sentence.Text,
            InformativeType = finalType,
            Structure = g.Structure,
            Voice = localTags.Voice,
            HasCitation = localTags.ClaimsCitation,
            Confidence = confidence,
            IsGrammaticallyCorrect = localTags.IsGrammaticallyCorrect,
            HasPronoun = localTags.HasPronoun,
            IsPlagiarized = g.IsPlagiarized,
            InfoQuality = g.InfoQuality,
            FunctionalType = localTags.FunctionalType,
            RelevanceScore = localTags.RelevanceScore,
            HtmlTag = g.HtmlTag,
             //ClaritySynthesisType = g.ClaritySynthesisType,
             //FactRetrievalType = g.FactRetrievalType,
             Grammar = localTags.IsGrammaticallyCorrect ? "Correct" : "Incorrect",
             ParagraphId = g.ParagraphId,

             
        };
    }
}
