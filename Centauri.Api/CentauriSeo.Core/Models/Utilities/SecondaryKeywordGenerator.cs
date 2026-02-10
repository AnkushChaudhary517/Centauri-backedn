using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Utilities
{
    public class SecondaryKeywordGenerator
    {
        static readonly HashSet<string> StopWords = new HashSet<string>
{
    "the","is","are","a","an","and","or","of","to","for","with",
    "in","on","by","how","what","when","who","do","does","you",
    "your","from","if"
};

        public static List<string> ExtractSecondaryKeywords(
    string primaryKeyword,
    List<string> headings)
        {
            var primaryTokens = Tokenize(primaryKeyword).ToHashSet();
            var phraseFrequency = new Dictionary<string, int>();

            foreach (var heading in headings)
            {
                var tokens = Tokenize(heading);
                var ngrams = GenerateNGrams(tokens);

                foreach (var phrase in ngrams)
                {
                    // Skip exact primary keyword
                    if (phrase == primaryKeyword.ToLower())
                        continue;

                    // Check overlap with primary keyword
                    var phraseTokens = phrase.Split(' ');
                    bool overlaps = phraseTokens.Any(t => primaryTokens.Contains(t));

                    if (!overlaps)
                        continue;

                    if (!phraseFrequency.ContainsKey(phrase))
                        phraseFrequency[phrase] = 0;

                    phraseFrequency[phrase]++;
                }
            }

            // Rank by frequency & length (SEO prefers meaningful phrases)
            return phraseFrequency
                .OrderByDescending(kv => kv.Value)
                .ThenByDescending(kv => kv.Key.Split(' ').Length)
                .Select(kv => kv.Key)
                .Distinct()
                .Take(15)
                .ToList();
        }

        static List<string> GenerateNGrams(List<string> tokens, int min = 2, int max = 4)
        {
            var ngrams = new List<string>();

            for (int n = min; n <= max; n++)
            {
                for (int i = 0; i <= tokens.Count - n; i++)
                {
                    ngrams.Add(string.Join(" ", tokens.Skip(i).Take(n)));
                }
            }
            return ngrams;
        }
        static List<string> Tokenize(string text)
        {
            return Regex.Replace(text.ToLower(), @"[^a-z0-9\s]", "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => !StopWords.Contains(t))
                .ToList();
        }


    }
}
