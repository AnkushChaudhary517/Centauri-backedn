using CentauriSeo.Core.Models.Enums;
using CentauriSeo.Core.Models.Outputs;

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
    //public FactRetrievalType FactRetrievalType { get; set; }
    public ClaritySynthesisType ClaritySynthesisType { get; set; }
    public bool ClaimsCitation { get; set; }
    public bool IsGrammaticallyCorrect { get; set; }
    public bool HasPronoun { get; set; }
    public bool IsPlagiarized { get; set; }
    public string ParagraphId { get; set; } = "";
    public double RelevanceScore { get; set; }
    public int AnswerSentenceFlag { get; set; } = 0;
    public int EntityConfidenceFlag { get; set; } = 0;

    public EntityMentionFlag EntityMentionFlag { get; set; }
}

public class ChatgptGeminiSentenceTag
{
    public string SentenceId { get; set; } = "";
    public SentenceStructure Structure { get; set; }
    public VoiceType Voice { get; set; }
    public FunctionalType FunctionalType { get; set; }
    public InformativeType InformativeType { get; set; }
    public double Confidence { get; set; }
}
