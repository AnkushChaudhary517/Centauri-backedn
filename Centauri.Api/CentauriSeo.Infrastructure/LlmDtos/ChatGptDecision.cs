using CentauriSeo.Core.Models.Enums;

namespace CentauriSeo.Infrastructure.LlmDtos;

public class ChatGptDecision
{
    public string SentenceId { get; set; } = "";
    public InformativeType FinalType { get; set; }
    public double Confidence { get; set; }
    public string Reason { get; set; } = "";
}
