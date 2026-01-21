

using CentauriSeo.Core.Models.Enums;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Infrastructure.LlmDtos;

namespace CentauriSeo.Application.Pipeline;

public static class Phase2_ArbitrationEngine
{
    public static ValidatedSentence Arbitrate(
        Sentence sentence,
        PerplexitySentenceTag p,
        GeminiSentenceTag g,
        ChatgptGeminiSentenceTag? aiDecision)
    {
        InformativeType finalType;
        double confidence = 0.7;

        FunctionalType functionalType = g.FunctionalType;
        if(p.FunctionalType != g.FunctionalType && aiDecision?.FunctionalType != null)
          functionalType = aiDecision.FunctionalType;

        VoiceType voiceType = g.Voice;
        if (p.Voice != g.Voice && aiDecision?.Voice != null)
            voiceType = aiDecision.Voice;

        // Rule 1: Statistic must contain number
        if ((p.InformativeType == InformativeType.Statistic ||
             g.InformativeType == InformativeType.Statistic)
            && sentence.Text.Any(char.IsDigit))
        {
            finalType = InformativeType.Statistic;
            confidence = 0.95;
        }
        // Rule 2: Prediction beats claim
        else if (p.InformativeType == InformativeType.Prediction ||
                 g.InformativeType == InformativeType.Prediction)
        {
            finalType = InformativeType.Prediction;
            confidence = 0.9;
        }
        // Rule 3: Opinion requires belief marker
        else if ((sentence.Text.Contains("I think") ||
                  sentence.Text.Contains("we believe")) &&
                 (p.InformativeType == InformativeType.Opinion ||
                  g.InformativeType == InformativeType.Opinion))
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
            finalType = g.InformativeType;
        }

        return new ValidatedSentence
        {
            Id = sentence.Id,
            Text = sentence.Text,
            InformativeType = finalType,
            Structure = g.Structure,
            Voice = voiceType,
            HasCitation = p.ClaimsCitation,
            Confidence = confidence,
            IsGrammaticallyCorrect = p.IsGrammaticallyCorrect,
            HasPronoun = p.HasPronoun,
            IsPlagiarized = g.IsPlagiarized,
            InfoQuality = g.InfoQuality,
            FunctionalType = functionalType,
             HtmlTag = g.HtmlTag,
             //ClaritySynthesisType = g.ClaritySynthesisType,
             //FactRetrievalType = g.FactRetrievalType,
             Grammar = p.IsGrammaticallyCorrect ? "Correct" : "Incorrect",
             ParagraphId = g.ParagraphId,
             
        };
    }
}
