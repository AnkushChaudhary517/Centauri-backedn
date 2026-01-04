using System.Text;
using CentauriSeo.Core.Models.Sentences;

namespace CentauriSeo.Infrastructure.Prompts;

public static class PerplexityPromptBuilder
{
    public static string Build(IEnumerable<Sentence> sentences)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Classify each sentence by InformativeType and citation presence.");
        sb.AppendLine("Return JSON array with: sentenceId, informativeType, claimsCitation.");

        foreach (var s in sentences)
            sb.AppendLine($"{s.Id}: {s.Text}");

        return sb.ToString();
    }
}
