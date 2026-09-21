using System.Net.Http.Headers;
using System.Text.Json;
using Centauri.ContentArchitect.Backend.Configuration;
using Centauri.ContentArchitect.Backend.Models;
using Microsoft.Extensions.Options;

namespace Centauri.ContentArchitect.Backend.Services.Clients;

public sealed class GoogleSearchConsoleClient : ISearchConsoleClient
{
    private readonly HttpClient _http;
    private readonly SearchConsoleOptions _options;

    public GoogleSearchConsoleClient(IHttpClientFactory factory, IOptions<SearchConsoleOptions> options)
    {
        _http = factory.CreateClient();
        _options = options.Value;
    }

    public async Task<List<GscInspection>> InspectUrlsAsync(string siteUrl, IReadOnlyList<string> urls, CancellationToken ct)
    {
        EnsureEnabled();
        var token = await GetAccessTokenAsync(ct);
        var result = new List<GscInspection>();

        foreach (var url in urls)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://searchconsole.googleapis.com/v1/urlInspection/index:inspect");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new
            {
                inspectionUrl = url,
                siteUrl = string.IsNullOrWhiteSpace(siteUrl) ? _options.SiteUrl : siteUrl,
                languageCode = "en-US"
            });

            using var response = await _http.SendAsync(req, ct);
            if (!response.IsSuccessStatusCode)
                continue;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var inspection = doc.RootElement.GetProperty("inspectionResult").GetProperty("indexStatusResult");

            var verdict = inspection.TryGetProperty("verdict", out var v) ? v.GetString() : "";
            var pageFetch = inspection.TryGetProperty("pageFetchState", out var pf) ? pf.GetString() : "";
            var userCanonical = inspection.TryGetProperty("userCanonical", out var uc) ? uc.GetString() : "";
            var googleCanonical = inspection.TryGetProperty("googleCanonical", out var gc) ? gc.GetString() : "";

            result.Add(new GscInspection
            {
                Url = url,
                Indexed = string.Equals(verdict, "PASS", StringComparison.OrdinalIgnoreCase),
                CrawlOk = string.Equals(pageFetch, "SUCCESSFUL", StringComparison.OrdinalIgnoreCase),
                CanonicalConsistent = !string.IsNullOrWhiteSpace(userCanonical) &&
                                      !string.IsNullOrWhiteSpace(googleCanonical) &&
                                      string.Equals(userCanonical, googleCanonical, StringComparison.OrdinalIgnoreCase)
            });
        }
        return result;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["refresh_token"] = _options.RefreshToken,
                ["grant_type"] = "refresh_token"
            })
        };
        using var response = await _http.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    private void EnsureEnabled()
    {
        if (!_options.Enabled) throw new InvalidOperationException("Search Console is disabled in appsettings.");
        if (string.IsNullOrWhiteSpace(_options.ClientId) ||
            string.IsNullOrWhiteSpace(_options.ClientSecret) ||
            string.IsNullOrWhiteSpace(_options.RefreshToken))
            throw new InvalidOperationException("Search Console OAuth credentials are missing.");
    }
}
