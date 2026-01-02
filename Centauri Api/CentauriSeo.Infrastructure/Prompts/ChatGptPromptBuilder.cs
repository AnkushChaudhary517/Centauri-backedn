using System.Text;
using CentauriSeo.Infrastructure.LlmDtos;

namespace CentauriSeo.Infrastructure.Prompts;

public static class ChatGptPromptBuilder
{
    public static string Build(
        IEnumerable<PerplexitySentenceTag> perplexity,
        IEnumerable<GeminiSentenceTag> gemini)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the arbitration authority.");
        sb.AppendLine("Resolve conflicts using SEO rules:");
        sb.AppendLine("1. Statistic requires number.");
        sb.AppendLine("2. Prediction overrides claim.");
        sb.AppendLine("3. Opinion requires first-person belief.");
        sb.AppendLine("4. Citation overrides uncertainty.");
        sb.AppendLine("Return JSON with sentenceId, finalType, confidence.");

        sb.AppendLine("PERPLEXITY:");
        sb.AppendLine(System.Text.Json.JsonSerializer.Serialize(perplexity));

        sb.AppendLine("GEMINI:");
        sb.AppendLine(System.Text.Json.JsonSerializer.Serialize(gemini));

        return sb.ToString();
    }
}
