using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Infrastructure.LlmDtos;

namespace CentauriSeo.Application.Pipeline;

public static class Phase2_Validator
{
    // Uses the existing Phase2_ArbitrationEngine to produce a ValidatedSentence.
    // Signature accepts concrete DTOs produced by Perplexity/Gemini/ChatGPT clients.
    //public static ValidatedSentence Validate(
    //    Sentence sentence,
    //    PerplexitySentenceTag perplexity,
    //    GeminiSentenceTag gemini,
    //    ChatgptGeminiSentenceTag? chatGpt = null)
    //{
    //    // Delegate to the central arbitration engine which implements the document rules.
    //    return Phase2_ArbitrationEngine.Arbitrate(sentence, perplexity, gemini, chatGpt);
    //}
}
