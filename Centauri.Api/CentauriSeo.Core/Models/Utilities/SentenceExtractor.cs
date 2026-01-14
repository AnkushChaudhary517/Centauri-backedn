using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Utilities
{
    using CentauriSeo.Core.Models.Sentences;
    using HtmlAgilityPack;
    using System.Text.RegularExpressions;
    using System.Xml;

    public static class SentenceExtractor
    {
        private static readonly HashSet<string> AllowedTags = new()
    {
        "h1", "h2", "h3", "h4", "h5", "h6",
        "p", "li", "td", "th", "img", "span","meta"
    };

        public static string GetCleanText(string htmlContent)
        {
            if (string.IsNullOrEmpty(htmlContent))
                return string.Empty;

            return Regex.Replace(htmlContent, @"\s+(id|class|style|data-[\w-]+)\s*=\s*""[^""]*""", "", RegexOptions.IgnoreCase);
            
        }

        public static List<ValidatedSentence> Extract(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return new();

            return LooksLikeHtml(content)
                ? ExtractFromHtml(content)
                : ExtractFromPlainText(content);
        }

        private static bool IsValidSentence(string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence))
                return false;

            // Remove punctuation
            var cleaned = Regex.Replace(sentence, @"[^\w\s]", "").Trim();

            // Must contain at least one letter
            if (!Regex.IsMatch(cleaned, @"[a-zA-Z]"))
                return false;

            // Reject pure numbering like "1", "1.", "a.", "i."
            if (Regex.IsMatch(cleaned, @"^(?:\d+|[a-zA-Z])$"))
                return false;

            // Minimum length safeguard
            if (cleaned.Length < 3)
                return false;

            return true;
        }


        // ---------- HTML ----------
        private static List<ValidatedSentence> ExtractFromHtml(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var sentences = new List<ValidatedSentence>();
            int index = 1;

            var nodes = doc.DocumentNode
                .Descendants()
                .Where(n => AllowedTags.Contains(n.Name.ToLowerInvariant()));

            foreach (var node in nodes)
            {
                var tag = node.Name.ToLowerInvariant();
                var text = HtmlEntity.DeEntitize(node.InnerText).Trim();

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                foreach (var sentence in SplitSentences(text))
                {
                    sentences.Add(new ValidatedSentence
                    {
                        Id = $"S{index++}",
                        Text = sentence,
                        HtmlTag = tag
                    });
                }
            }

            return sentences;
        }

        // ---------- PLAIN TEXT ----------
        private static List<ValidatedSentence> ExtractFromPlainText(string text)
        {
            var sentences = new List<ValidatedSentence>();
            int index = 1;

            foreach (var sentence in SplitSentences(text))
            {
                sentences.Add(new ValidatedSentence
                {
                    Id = $"S{index++}",
                    Text = sentence,
                    HtmlTag = "p"
                });
            }

            return sentences;
        }

        // ---------- SENTENCE SPLITTING ----------
        private static IEnumerable<string> SplitSentences(string text)
        {
            return Regex
                .Split(text, @"(?<=[.!?])\s+")
                .Select(s => s.Trim())
                .Where(IsValidSentence);
        }

        // ---------- HEURISTIC ----------
        private static bool LooksLikeHtml(string input)
        {
            return Regex.IsMatch(input, @"<\s*\w+[^>]*>");
        }
    }

}
