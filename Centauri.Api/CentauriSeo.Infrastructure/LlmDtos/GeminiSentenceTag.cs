using CentauriSeo.Core.Models.Enums;

namespace CentauriSeo.Infrastructure.LlmDtos;

public class GeminiSentenceTag
{
    public string SentenceId { get; set; } = "";
    public string Sentence { get; set; } = "";
    public string HtmlTag { get; set; } = "";
    public SentenceStructure Structure { get; set; }
    public VoiceType Voice { get; set; }
    public InfoQuality InfoQuality { get; set; }
    public FunctionalType FunctionalType { get; set; }
    public InformativeType InformativeType { get; set; }
    public bool ClaimsCitation { get; set; }
    public bool IsGrammaticallyCorrect { get; set; }
    public bool HasPronoun { get; set; }
    public bool IsPlagiarized { get; set; }
}
