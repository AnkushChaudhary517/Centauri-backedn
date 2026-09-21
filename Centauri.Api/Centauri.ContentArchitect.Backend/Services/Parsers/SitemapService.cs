using System.Xml.Linq;
using Centauri.ContentArchitect.Backend.Configuration;
using Microsoft.Extensions.Options;

namespace Centauri.ContentArchitect.Backend.Services.Parsers;

public sealed class SitemapService : ISitemapService
{
    private readonly HttpClient _http = new();
    private readonly AnalysisOptions _options;
    public SitemapService(IOptions<AnalysisOptions> options) => _options = options.Value;

    public async Task<List<string>> GetUrlsAsync(string websiteUrl, int maxUrls, CancellationToken ct)
    {
        var baseUri = new Uri(websiteUrl);
        var robots = new Uri(baseUri, "/robots.txt").ToString();
        string? sitemap = null;

        try
        {
            var robotsText = await _http.GetStringAsync(robots, ct);
            sitemap = robotsText.Split('\n')
                .Select(x => x.Trim())
                .FirstOrDefault(x => x.StartsWith("Sitemap:", StringComparison.OrdinalIgnoreCase))
                ?.Split(':', 2).ElementAtOrDefault(1)?.Trim();
        } catch { }

        sitemap ??= new Uri(baseUri, "/sitemap.xml").ToString();

        var xml = await _http.GetStringAsync(sitemap, ct);
        var doc = XDocument.Parse(xml);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var urls = doc.Descendants(ns + "url")
            .Select(x => x.Element(ns + "loc")?.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(maxUrls)
            .Cast<string>()
            .ToList();

        // If this is a sitemap index, follow child sitemaps.
        if (urls.Count == 0)
        {
            var children = doc.Descendants(ns + "sitemap")
                .Select(x => x.Element(ns + "loc")?.Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(20)
                .Cast<string>();

            foreach (var child in children)
            {
                if (urls.Count >= maxUrls) break;
                try
                {
                    var childXml = await _http.GetStringAsync(child, ct);
                    var childDoc = XDocument.Parse(childXml);
                    var childNs = childDoc.Root?.Name.Namespace ?? XNamespace.None;
                    urls.AddRange(childDoc.Descendants(childNs + "url")
                        .Select(x => x.Element(childNs + "loc")?.Value)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Cast<string>()
                        .Take(maxUrls - urls.Count));
                } catch { }
            }
        }

        return urls.Distinct(StringComparer.OrdinalIgnoreCase).Take(maxUrls).ToList();
    }
}
