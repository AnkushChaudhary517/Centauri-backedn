
using CentauriSeo.Core.Models.Enums;

namespace CentauriSeo.Infrastructure.LlmDtos;

public class PerplexitySentenceTag
{
    public string SentenceId { get; set; } = "";
    public InformativeType InformativeType { get; set; }
    public bool ClaimsCitation { get; set; }
}
