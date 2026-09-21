using System.Net;
using System.Text.RegularExpressions;
using Centauri.ContentArchitect.Backend.Models;
using HtmlAgilityPack;

namespace Centauri.ContentArchitect.Backend.Services.Clients;

public sealed class PublicIndexabilityClient : IPublicIndexabilityClient
{
    private readonly HttpClient _http;

    public PublicIndexabilityClient(IHttpClientFactory factory)
    {
        _http = factory.CreateClient();
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<List<PublicIndexabilityInspection>> InspectUrlsAsync(string websiteUrl, IReadOnlyList<string> urls, CancellationToken ct)
    {
        var baseUri = new Uri(websiteUrl);
        var robots = await GetRobotsAsync(baseUri, ct);
        var inspections = new List<PublicIndexabilityInspection>();

        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var pageUri) ||
                !string.Equals(pageUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
                continue;

            var blocked = IsBlockedByRobots(robots, pageUri.AbsolutePath);
            try
            {
                using var response = await _http.GetAsync(pageUri, ct);
                var html = await response.Content.ReadAsStringAsync(ct);
                var headers = string.Join(",", response.Headers.TryGetValues("X-Robots-Tag", out var values) ? values : Array.Empty<string>());
                var document = new HtmlDocument();
                document.LoadHtml(html);
                var robotsMeta = document.DocumentNode.SelectSingleNode("//meta[translate(@name, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz')='robots']")
                    ?.GetAttributeValue("content", "") ?? "";
                var canonical = document.DocumentNode.SelectSingleNode("//link[translate(@rel, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz')='canonical']")
                    ?.GetAttributeValue("href", "");

                var noindex = ContainsNoindex(headers) || ContainsNoindex(robotsMeta);
                var canonicalConsistent = string.IsNullOrWhiteSpace(canonical) ||
                    Uri.TryCreate(pageUri, canonical, out var canonicalUri) &&
                    string.Equals(canonicalUri.Host, pageUri.Host, StringComparison.OrdinalIgnoreCase);

                inspections.Add(new PublicIndexabilityInspection
                {
                    Url = url,
                    CrawlOk = response.IsSuccessStatusCode,
                    IsBlockedByRobots = blocked,
                    HasNoindex = noindex,
                    CanonicalConsistent = canonicalConsistent
                });
            }
            catch
            {
                inspections.Add(new PublicIndexabilityInspection { Url = url, IsBlockedByRobots = blocked });
            }
        }

        return inspections;
    }

    private async Task<string> GetRobotsAsync(Uri baseUri, CancellationToken ct)
    {
        try { return await _http.GetStringAsync(new Uri(baseUri, "/robots.txt"), ct); }
        catch { return ""; }
    }

    private static bool IsBlockedByRobots(string robots, string path)
    {
        var appliesToAll = false;
        foreach (var rawLine in robots.Split('\n'))
        {
            var line = rawLine.Split('#')[0].Trim();
            if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
            {
                appliesToAll = string.Equals(line.Split(':', 2)[1].Trim(), "*", StringComparison.Ordinal);
            }
            else if (appliesToAll && line.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase))
            {
                var rule = line.Split(':', 2)[1].Trim();
                if (rule == "/" || (!string.IsNullOrEmpty(rule) && path.StartsWith(rule, StringComparison.OrdinalIgnoreCase))) return true;
            }
        }
        return false;
    }

    private static bool ContainsNoindex(string value) => Regex.IsMatch(value, @"\bnoindex\b", RegexOptions.IgnoreCase);
}
