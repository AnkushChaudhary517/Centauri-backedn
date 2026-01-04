using System.Collections.Generic;
using System.Linq;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Infrastructure.LlmDtos;
using CentauriSeo.Core.Models.Utilities;

namespace CentauriSeo.Application.Pipeline;

public class Phase1And2Orchestrator
{
    // Existing Execute method kept for low-level use
    public IReadOnlyList<ValidatedSentence> Execute(
        IReadOnlyList<Sentence> sentences,
        IReadOnlyList<PerplexitySentenceTag> perplexity,
        IReadOnlyList<GeminiSentenceTag> gemini,
        IReadOnlyList<ChatGptDecision>? chatGpt = null)
    {
        var result = new List<ValidatedSentence>();

        foreach (var s in sentences)
        {
            var p = perplexity.Single(x => x.SentenceId == s.Id);
            var g = gemini.Single(x => x.SentenceId == s.Id);
            var ai = chatGpt?.SingleOrDefault(x => x.SentenceId == s.Id);

            result.Add(Phase2_ArbitrationEngine.Arbitrate(s, p, g, ai));
        }

        return result;
    }

    // New high-level entry used by controllers: builds deterministic tags using repo detectors,
    // then runs Execute -> Phase2 arbitration to return a validated sentence map.
    public static IReadOnlyList<ValidatedSentence> Run(CentauriSeo.Core.Models.Input.ArticleInput article)
    {
        var sentences = Phase0_InputParser.Parse(article);

        var perplexityTags = sentences.Select(s => new PerplexitySentenceTag
        {
            SentenceId = s.Id,
            InformativeType = InformativeTypeDetector.Detect(s.Text),
            ClaimsCitation = CitationDetector.HasCitation(s.Text)
        }).ToList();

        var geminiTags = sentences.Select(s => new GeminiSentenceTag
        {
            SentenceId = s.Id,
            Structure = StructureDetector.Detect(s.Text),
            Voice = VoiceDetector.Detect(s.Text),
            InformativeType = InformativeTypeDetector.Detect(s.Text)
        }).ToList();

        // No ChatGPT decisions available in deterministic run; pass null
        var orchestrator = new Phase1And2Orchestrator();
        return orchestrator.Execute(sentences, perplexityTags, geminiTags, null);
    }
}
