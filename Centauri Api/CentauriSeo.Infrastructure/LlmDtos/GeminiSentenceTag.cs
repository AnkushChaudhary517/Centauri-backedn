using CentauriSeo.Core.Models.Enums;

namespace CentauriSeo.Infrastructure.LlmDtos;

public class GeminiSentenceTag
{
    public string SentenceId { get; set; } = "";
    public SentenceStructure Structure { get; set; }
    public VoiceType Voice { get; set; }
    public InformativeType InformativeType { get; set; }
    public bool ClaimsCitation { get; set; }
}
