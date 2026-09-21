using System.Net;
using System.Text.RegularExpressions;
using Centauri.ContentArchitect.Backend.Models;
using HtmlAgilityPack;

namespace Centauri.ContentArchitect.Backend.Services.Parsers;

public sealed class HtmlWebPageParser : IWebPageParser
{
    private readonly HttpClient _http = new();

    public async Task<PageAnalysis> ParseAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        var html = await response.Content.ReadAsStringAsync(ct);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        foreach (var node in doc.DocumentNode.SelectNodes("//script|//style|//noscript") ?? Enumerable.Empty<HtmlNode>())
            node.Remove();

        var headings = doc.DocumentNode.SelectNodes("//h1|//h2|//h3")
            ?.Select(x => WebUtility.HtmlDecode(x.InnerText).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList() ?? new List<string>();

        var text = WebUtility.HtmlDecode(doc.DocumentNode.InnerText);
        text = Regex.Replace(text, @"\s+", " ").Trim();

        var questions = headings.Where(IsQuestion).ToList();
        var sourceCount = doc.DocumentNode.SelectNodes("//a[@href]")
            ?.Count(x => x.GetAttributeValue("href", "").StartsWith("http", StringComparison.OrdinalIgnoreCase)) ?? 0;

        return new PageAnalysis
        {
            Url = url,
            Text = text,
            Headings = headings,
            Questions = questions,
            SourceDensity = sourceCount / Math.Max(1.0, text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length / 1000.0)
        };
    }

    private static bool IsQuestion(string h)
        => h.EndsWith("?") || Regex.IsMatch(h, @"^(what|why|how|when|where|which|who|can|is|are|does|do|should|will)\b", RegexOptions.IgnoreCase);
}
