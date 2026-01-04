using CentauriSeo.Core.Models.Enums;

namespace CentauriSeo.Core.Models.Utilities
{

    public class Level1Sentence
    {
        public string Id { get; init; } = "";
        public string Text { get; init; } = "";

        public SentenceStructure Structure { get; init; }
        public VoiceType Voice { get; init; }
        public InformativeType InformativeType { get; init; }

        public bool HasCitation { get; init; }
        public bool IsGrammaticallyCorrect { get; init; }
        public bool HasPronoun { get; init; }

        public InfoQuality InfoQuality { get; init; }
        public bool IsPlagiarized { get; init; }
    }

}
