using CentauriSeo.Core.Models.Sentences;
using System.Text.RegularExpressions;

namespace CentauriSeo.Core.Models.Utilities
{

    public static class SentenceTokenizer
    {
        private static readonly Regex SentenceRegex =
            new(@"(?<=[.!?])\s+(?=[A-Z])", RegexOptions.Compiled);

        public static IReadOnlyList<Sentence> Tokenize(string raw)
        {
            var paragraphs = raw.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
            var result = new List<Sentence>();
            int id = 1;

            for (int p = 0; p < paragraphs.Length; p++)
            {
                var sentences = SentenceRegex.Split(paragraphs[p].Trim());
                foreach (var s in sentences)
                {
                    var text = s.Trim();
                    if (text.Length < 3) continue;

                    result.Add(new Sentence($"S{id++}", text, p));
                }
            }

            return result;
        }
    }

}
