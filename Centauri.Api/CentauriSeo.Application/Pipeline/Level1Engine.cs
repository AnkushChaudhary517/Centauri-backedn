using System.Collections.Generic;
using System.Linq;
using CentauriSeo.Core.Models.Input;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;

namespace CentauriSeo.Application.Pipeline
{
    public static class Level1Engine
    {
        // Use the Phase0 parser that already exists in the repo and map to Level1Sentence
        //public static IReadOnlyList<Level1Sentence> Analyze(ArticleInput article)
        //{
        //    if (article == null || string.IsNullOrWhiteSpace(article.Raw))
        //        return new List<Level1Sentence>();

        //    var sentences = Phase0_InputParser.Parse(article);

        //    return sentences.Select(s => new Level1Sentence
        //    {
        //        Id = s.Id,
        //        Text = s.Text,
        //        Structure = StructureDetector.Detect(s.Text),
        //        Voice = VoiceDetector.Detect(s.Text),
        //        InformativeType = InformativeTypeDetector.Detect(s.Text),
        //        HasCitation = CitationDetector.HasCitation(s.Text),
        //        IsGrammaticallyCorrect = GrammarDetector.IsCorrect(s.Text),
        //        HasPronoun = PronounDetector.ContainsPronoun(s.Text),
        //        InfoQuality = InfoQualityClassifier.Classify(s.Text),
        //        IsPlagiarized = PlagiarismEngine.IsPlagiarized(s.Text)
        //    }).ToList();
        //}
    }
}
