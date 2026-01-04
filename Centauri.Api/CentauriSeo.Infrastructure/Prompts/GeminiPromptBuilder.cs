using System.Text;
using CentauriSeo.Core.Models.Sentences;

namespace CentauriSeo.Infrastructure.Prompts;

public static class GeminiPromptBuilder
{
    public static string Build(IEnumerable<Sentence> sentences)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Analyze linguistic structure and voice.");
        sb.AppendLine("Return JSON array with: sentenceId, structure, voice, informativeType.");

        foreach (var s in sentences)
            sb.AppendLine($"{s.Id}: {s.Text}");

        return sb.ToString();
    }
}
