using System.Text.RegularExpressions;

namespace Centauri.ContentArchitect.Backend.Extensions;

/// <summary>
/// Removes duplicate questions in list order. Questions in an earlier list take precedence
/// over equivalent questions in later lists.
/// </summary>
public static class QuestionDeduplicationExtensions
{
    private static readonly Regex WordPattern = new("[a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "are", "as", "at", "can", "does", "do", "for", "how", "i", "in", "is", "it", "my", "of", "on", "or", "should", "some", "the", "to", "what", "when", "which", "who", "why", "with", "your"
    };
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cost"] = "price", ["pricing"] = "price", ["free"] = "price",
        ["examples"] = "example", ["tools"] = "tool", ["software"] = "tool", ["systems"] = "system",
        ["functions"] = "function", ["features"] = "feature", ["benefits"] = "benefit",
        ["choosing"] = "choose", ["selection"] = "choose", ["uses"] = "use"
    };

    public static List<string> DeduplicateQuestions(this IEnumerable<IEnumerable<string>?> questionLists)
    {
        var retained = new List<QuestionSignature>();
        foreach (var questions in questionLists)
        {
            if (questions is null) continue;
            foreach (var rawQuestion in questions)
            {
                var question = rawQuestion?.Trim();
                if (string.IsNullOrWhiteSpace(question)) continue;

                var candidate = QuestionSignature.Create(question);
                if (retained.All(existing => !existing.IsEquivalentTo(candidate)))
                    retained.Add(candidate);
            }
        }

        return retained.Select(x => x.Original).ToList();
    }

    private sealed record QuestionSignature(string Original, string Intent, HashSet<string> Topic)
    {
        public static QuestionSignature Create(string question)
        {
            var words = WordPattern.Matches(question.ToLowerInvariant())
                .Select(x => Synonyms.TryGetValue(x.Value, out var synonym) ? synonym : x.Value)
                .ToList();
            var intent = GetIntent(words);
            var topic = words.Where(x => !StopWords.Contains(x) && !IntentWords.Contains(x) && !x.All(char.IsDigit)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new QuestionSignature(question, intent, topic);
        }

        public bool IsEquivalentTo(QuestionSignature other)
        {
            if (Original.Equals(other.Original, StringComparison.OrdinalIgnoreCase)) return true;
            if (!Intent.Equals(other.Intent, StringComparison.Ordinal)) return false;
            if (Topic.SetEquals(other.Topic)) return true;
            if (Topic.Count == 0 || other.Topic.Count == 0) return false;

            var overlap = Topic.Intersect(other.Topic, StringComparer.OrdinalIgnoreCase).Count();
            var similarity = (double)overlap / (Topic.Count + other.Topic.Count - overlap);
            // Intent must already match. A high topical overlap catches paraphrases while
            // preserving different needs such as features vs. choosing a product.
            return similarity >= 0.60 || (Topic.Count == 1 && other.Topic.Count == 1 && overlap == 1);
        }

        private static string GetIntent(IReadOnlyCollection<string> words)
        {
            if (words.Contains("price")) return "pricing";
            if (words.Contains("choose") || words.Contains("look")) return "selection";
            if (words.Contains("type")) return "types";
            if (words.Contains("feature")) return "features";
            if (words.Contains("benefit") || words.Contains("important")) return "benefits";
            if (words.Contains("implement") || words.Contains("started")) return "implementation";
            if (words.Contains("example") || words.Contains("top") || words.Contains("popular") || words.Contains("best")) return "recommendations";
            if (words.Contains("who")) return "audience";
            if (words.Contains("why")) return "rationale";
            if (words.Contains("how") || words.Contains("function") || words.Contains("work") || words.Contains("do")) return "operation";
            return "definition";
        }

        private static readonly HashSet<string> IntentWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "price", "choose", "look", "type", "feature", "benefit", "important", "implement", "started", "example", "top", "popular", "best", "who", "why", "how", "function", "work", "do"
        };
    }
}
