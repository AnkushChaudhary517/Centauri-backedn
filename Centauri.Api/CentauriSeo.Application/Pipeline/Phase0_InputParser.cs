using CentauriSeo.Core.Models.Input;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Infrastructure.LlmDtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace CentauriSeo.Application.Pipeline;

public static class Phase0_InputParser
{
    //public static List<Sentence> Parse(ArticleInput article)
    //{
    //    var paragraphs = article.Raw.Split("\n\n");
    //    var sentences = new List<Sentence>();
    //    int id = 1;

    //    for (int p = 0; p < paragraphs.Length; p++)
    //    {
    //        var parts = paragraphs[p]
    //            .Split(new[] { ".", "?", "!" }, StringSplitOptions.RemoveEmptyEntries);

    //        foreach (var part in parts)
    //        {
    //            sentences.Add(new Sentence($"S{id++}", part.Trim(), p));
    //        }
    //    }

    //    return sentences;
    //}



public static List<GeminiSentenceTag> TagArticleComplete(string fullContent)
{
    var result = new List<GeminiSentenceTag>();

    // Split by double newlines but capture the newlines to ensure no data loss
    var paragraphBlocks = Regex.Split(fullContent, @"(\r\n\r\n|\n\n)");

    int sCount = 1;
    int pCount = 1;

    foreach (var block in paragraphBlocks)
    {
        if (string.IsNullOrWhiteSpace(block)) continue;

        // Regex that captures the sentence AND the trailing punctuation/whitespace
        var sentences = Regex.Matches(block, @"[^.!?\n]+[.!?]*\s*");

        foreach (Match match in sentences)
        {
            string rawSentence = match.Value;
            string cleanText = rawSentence.Trim();

            // Tagging Logic
            string htmlTag = "p";
            if (sCount == 1) htmlTag = "h1";
            else if (Regex.IsMatch(cleanText, @"^[ivx]+\)", RegexOptions.IgnoreCase)) htmlTag = "h3";
            else if (cleanText.Length < 65 && !cleanText.EndsWith(".") && !cleanText.Contains(",")) htmlTag = "h2";
            else if (cleanText.Contains(",") && cleanText.Contains("/")) htmlTag = "td";
            else if (cleanText.StartsWith("Form")) htmlTag = "li";

            result.Add(new GeminiSentenceTag
            {
                SentenceId = $"S{sCount++}",
                Sentence = rawSentence, // Complete raw text
                HtmlTag = htmlTag,
                ParagraphId = $"P{pCount}"
            });
        }
        pCount++;
    }
    return result;
}

    public static List<GeminiSentenceTag> TagArticleProfessional(string fullContent)
    {
        var result = new List<GeminiSentenceTag>();

        var tagRegex = new Regex(
            @"<(h[1-6]|p|li|td|th)[^>]*>(.*?)</\1>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        int sCount = 1;
        int pCount = 1;

        var matches = tagRegex.Matches(fullContent);

        // ✅ Case 1: No HTML tags → treat entire content as a <p>
        if (matches.Count == 0)
        {
            string raw = NormalizeHtml(fullContent);

            if (!string.IsNullOrWhiteSpace(raw))
            {
                foreach (var sentence in SplitSentencesLossless(raw))
                {
                    var clean = sentence.Trim();

                    if (!IsValidSentence(clean))
                        continue;

                    result.Add(new GeminiSentenceTag
                    {
                        SentenceId = $"S{sCount++}",
                        Sentence = clean,
                        HtmlTag = "p",
                        ParagraphId = $"P{pCount}"
                    });
                }
            }

            return result;
        }

        foreach (Match match in tagRegex.Matches(fullContent))
        {
            string tag = match.Groups[1].Value.ToLower();
            string raw = NormalizeHtml(match.Groups[2].Value);

            if (string.IsNullOrWhiteSpace(raw))
                continue;

            foreach (var sentence in SplitSentencesLossless(raw))
            {
                var clean = sentence.Trim();

                if (!IsValidSentence(clean))
                    continue;

                result.Add(new GeminiSentenceTag
                {
                    SentenceId = $"S{sCount++}",
                    Sentence = clean,
                    HtmlTag = tag,
                    ParagraphId = $"P{pCount}"
                });
            }


            pCount++;
        }

        return result;
    }
    private static bool IsValidSentence(string text)
    {
        // Must contain at least one letter or digit
        return Regex.IsMatch(text, @"[A-Za-z0-9]");
    }

    private static string NormalizeHtml(string input)
    {
        input = Regex.Replace(input, @"<a[^>]*></a>", "", RegexOptions.IgnoreCase);
        input = Regex.Replace(input, @"<[^>]+>", "");

        // Normalize smart quotes
        input = input
            .Replace("“", "\"")
            .Replace("”", "\"")
            .Replace("’", "'")
            .Replace("‘", "'");

        return input.Trim();
    }

    private static IEnumerable<string> SplitSentencesLossless(string text)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text))
            yield break;

        // If no sentence-ending punctuation → return whole thing
        if (!Regex.IsMatch(text, @"[.!?]"))
        {
            yield return text;
            yield break;
        }

        // Otherwise split conservatively
        var parts = Regex.Matches(
            text,
            @"[^.!?]+[.!?]+|[^.!?]+$"
        );

        foreach (Match m in parts)
        {
            var s = m.Value.Trim();
            if (!string.IsNullOrEmpty(s))
                yield return s;
        }
    }

    private static string StripHtml(string input)
    {
        return Regex.Replace(input, "<.*?>", string.Empty);
    }
    private static void AddSentence(
        List<GeminiSentenceTag> list,
        string text,
        string tag,
        int paragraphId,
        ref int sentenceCount)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        list.Add(new GeminiSentenceTag
        {
            SentenceId = $"S{sentenceCount++}",
            Sentence = text.Trim(),
            HtmlTag = tag,
            ParagraphId = $"P{paragraphId}"
        });
    }

    private static IEnumerable<string> SplitSentences(string text, string abbrev)
    {
        // Protect decimals (3.5), acronyms (U.S.), and abbreviations
        text = Regex.Replace(text, @"(\b[A-Z])\.(?=[A-Z]\.)", "$1∯"); // U.S.
        text = Regex.Replace(text, @"(\d)\.(\d)", "$1∯$2");          // 3.5
        text = Regex.Replace(text, $@"\b({abbrev})\.", "$1∯");

        var matches = Regex.Matches(
            text,
            @"[^.!?]+[.!?]+(?=\s+[A-Z]|\s*$)"
        );

        foreach (Match m in matches)
        {
            yield return m.Value
                .Replace("∯", ".")
                .Trim();
        }
    }


    public static List<GeminiSentenceTag> TagByHtmlStructureStrict(string fullContent)
{
    var result = new List<GeminiSentenceTag>();

    // 1. Extract content within HTML tags
    string tagPattern = @"<(?<tag>[a-zA-Z0-9]+)[^>]*>(?<content>.*?)</\k<tag>>";
    var matches = Regex.Matches(fullContent, tagPattern, RegexOptions.Singleline);

    int sCount = 1;
    int pCount = 1;

    foreach (Match match in matches)
    {
        string tagName = match.Groups["tag"].Value.ToLower();
        string innerContent = match.Groups["content"].Value;

        // 2. Realistic Sentence Splitting (Gemini-Style)
        // Isme lookahead hai: Agar punctuation ke baad quote (") ya space + lowercase ho, toh mat todo.
        // Yeh "Ankush \"Chaudhary.\"" jaise cases ko handle karega.
        string sentencePattern = @"[^.!?\n]+([.!?]+(?=""|\s*[a-z])|(?<!"")[.!?]+(?!\s*[a-z])|$)";
        var sentences = Regex.Matches(innerContent, sentencePattern);

        foreach (Match sMatch in sentences)
        {
            string rawSentence = sMatch.Value;

            // 3. Final Step: Replace all remaining HTML tags with Empty String
            // Isse "my name is <b>Ankush</b>" se "my name is Ankush" ban jayega
            string cleanSentence = Regex.Replace(rawSentence, @"<[^>]*>", string.Empty);

            // Extra safety: whitespace clean
            if (string.IsNullOrWhiteSpace(cleanSentence)) continue;

            result.Add(new GeminiSentenceTag
            {
                SentenceId = $"S{sCount++}",
                Sentence = cleanSentence.Trim(),
                HtmlTag = tagName,
                ParagraphId = $"P{pCount}"
            });
        }
        pCount++;
    }

    return result;
}
public static List<GeminiSentenceTag> TagByHtmlStructure(string fullContent)
{
    var result = new List<GeminiSentenceTag>();

    // 1. Logic: Extract content between ANY html tags (h1, h2, p, li, etc.)
    // Isse <h1> aur <p> ke beech ka text kabhi merge nahi hoga.
    string pattern = @"<(?<tag>[a-zA-Z0-9]+)[^>]*>(?<content>.*?)</\k<tag>>";
    var matches = Regex.Matches(fullContent, pattern, RegexOptions.Singleline);

    int sCount = 1;
    int pCount = 1;

    foreach (Match match in matches)
    {
        string tagName = match.Groups["tag"].Value.ToLower();
        string innerContent = match.Groups["content"].Value.Trim();

        // 2. Logic: Ek tag ke andar multiple sentences ho sakte hain
        // Hum inner content ko split karenge par raw words/punctuation preserve rakhenge
        var sentences = Regex.Matches(innerContent, @"[^.!?]+[.!?]*\s*");

        foreach (Match sMatch in sentences)
        {
            string rawSentence = sMatch.Value;

            result.Add(new GeminiSentenceTag
            {
                SentenceId = $"S{sCount++}",
                Sentence = rawSentence, // Exact raw words
                HtmlTag = tagName,      // Tag from the original HTML (h1, p, etc.)
                ParagraphId = $"P{pCount}"
            });
        }

        // Har HTML tag (like <h1> or <p>) ek naya paragraph represent karta hai
        pCount++;
    }

    return result;
}

public static List<GeminiSentenceTag> TagArticleLikeGemini(string fullContent)
    {
        var result = new List<GeminiSentenceTag>();

        // 1. Semantic Break Logic: Gemini keywords aur source markers par break karo
        // Isse 'Meta Title' aur 'Slug' ek sentence mein merge nahi honge
        string pattern = @"(?=\|Meta Title:|Meta Description:|URL Slug:|Target Keyword:|\r\n|\n)";
        var segments = Regex.Split(fullContent, pattern)
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();

        int sCount = 1;
        int pCount = 1;

        foreach (var segment in segments)
        {
            // 2. Sentence Splitting: Preserve punctuation and spacing
            var sentences = Regex.Matches(segment, @"[^.!?\n]+[.!?]*\s*");

            foreach (Match match in sentences)
            {
                string rawText = match.Value;
                string cleanText = rawText.Trim();

                // 3. HTML Tagging Hierarchy
                string tag = "p";

                // H1: First meaningful sentence
                if (sCount == 1) tag = "h1";

                // H3: Sub-steps like i), ii), iii)
                else if (Regex.IsMatch(cleanText, @"^[ivx]+\)", RegexOptions.IgnoreCase)) tag = "h3";

                // H2: Section headers (Short, no period, no colon)
                else if (cleanText.Length < 70 && !cleanText.EndsWith(".") && !cleanText.Contains(":")) tag = "h2";

                // TD/LI: Special structures
                else if (cleanText.Contains("/") && cleanText.Contains(",")) tag = "td";
                else if (cleanText.StartsWith("Form")) tag = "li";

                result.Add(new GeminiSentenceTag
                {
                    SentenceId = $"S{sCount++}",
                    Sentence = rawText, // 100% original text preserved
                    HtmlTag = tag,
                    ParagraphId = $"P{pCount}"
                });
            }
            // Increment paragraph for every major semantic segment
            pCount++;
        }
        return result;
    }
}
