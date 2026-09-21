using CentauriSeo.Core.Models;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CentauriSeo.Infrastructure.Services
{
        public interface ISitemapService
        {
            Task<SitemapResult> DiscoverSitemapAsync(string targetUrl);
        }

        public class SitemapService : ISitemapService
        {
            private readonly HttpClient _httpClient;
            private static readonly string[] CandidateSitemapPaths = new[]
            {
            "/sitemap.xml",
            "/sitemap_index.xml",
            "/wp-sitemap.xml",
            "/post-sitemap.xml",
            "/page-sitemap.xml",
            "/sitemap-posts.xml",
            "/sitemap-1.xml"
        };

            private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".css", ".js", ".jpg", ".jpeg", ".png", ".gif", ".svg", ".pdf", ".webp", ".ico", ".woff", ".woff2", ".ttf"
        };

            public SitemapService(HttpClient httpClient)
            {
                _httpClient = httpClient;
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            }

            public async Task<SitemapResult> DiscoverSitemapAsync(string targetUrl)
            {
                var uri = new Uri(targetUrl.StartsWith("http") ? targetUrl : $"https://{targetUrl}");
                var baseUrl = $"{uri.Scheme}://{uri.Host}";
                var foundUrls = new HashSet<string>();
                string sourceType = "XmlSitemap";

                // A & B: Candidate Cascade & Recursive Sub-Sitemap Parsing
                foreach (var path in CandidateSitemapPaths)
                {
                    var sitemapUrl = $"{baseUrl}{path}";
                    try
                    {
                        var subUrls = await FetchAndParseSitemapXmlAsync(sitemapUrl);
                        if (subUrls.Any())
                        {
                            foreach (var url in subUrls) foundUrls.Add(url);
                        }
                    }
                    catch
                    {
                        // Continue checking candidate paths
                    }
                }

                // C: Fallback HTML Homepage Scraper
                if (!foundUrls.Any())
                {
                    sourceType = "HtmlCrawl";
                    try
                    {
                        var htmlUrls = await CrawlHomepageHtmlAsync(baseUrl);
                        foreach (var url in htmlUrls) foundUrls.Add(url);
                    }
                    catch
                    {
                        // Fallback yields empty list if domain is completely inaccessible
                    }
                }

                // D: Slug-to-Title Normalization
                var pages = foundUrls
                    .Select(NormalizeUrlToPage)
                    .Where(p => !string.IsNullOrEmpty(p.Title))
                    .ToList();

                return new SitemapResult
                {
                    Domain = uri.Host,
                    SourceType = sourceType,
                    Pages = pages
                };
            }

            private async Task<List<string>> FetchAndParseSitemapXmlAsync(string sitemapUrl)
            {
                var results = new List<string>();
                var response = await _httpClient.GetAsync(sitemapUrl);
                if (!response.IsSuccessStatusCode) return results;

                var xmlStream = await response.Content.ReadAsStreamAsync();
                var doc = XDocument.Load(xmlStream);

                XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

                // Check if it's a sitemapindex (<sitemapindex><sitemap><loc>...</loc></sitemap></sitemapindex>)
                var subSitemaps = doc.Descendants(ns + "sitemap").Select(x => x.Element(ns + "loc")?.Value).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (subSitemaps.Any())
                {
                    foreach (var subUrl in subSitemaps)
                    {
                        if (subUrl != null)
                        {
                            var childUrls = await FetchAndParseSitemapXmlAsync(subUrl);
                            results.AddRange(childUrls);
                        }
                    }
                    return results;
                }

                // Otherwise, extract regular page URLs (<urlset><url><loc>...</loc></url></urlset>)
                var urls = doc.Descendants(ns + "url")
                    .Select(x => x.Element(ns + "loc")?.Value)
                    .Where(x => !string.IsNullOrEmpty(x))
                    .Cast<string>();

                results.AddRange(urls);
                return results;
            }

            private async Task<List<string>> CrawlHomepageHtmlAsync(string baseUrl)
            {
                var results = new HashSet<string>();
                var html = await _httpClient.GetStringAsync(baseUrl);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var nodes = doc.DocumentNode.SelectNodes("//a[@href]");
                if (nodes == null) return results.ToList();

                foreach (var node in nodes)
                {
                    var href = node.GetAttributeValue("href", string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(href) || href.StartsWith("#") || href.StartsWith("javascript:")) continue;

                    if (Uri.TryCreate(new Uri(baseUrl), href, out var absoluteUri))
                    {
                        // Check domain match and asset filter
                        if (absoluteUri.Host.Equals(new Uri(baseUrl).Host, StringComparison.OrdinalIgnoreCase))
                        {
                            var ext = Path.GetExtension(absoluteUri.AbsolutePath);
                            if (!AssetExtensions.Contains(ext))
                            {
                                results.Add(absoluteUri.ToString());
                            }
                        }
                    }
                }

                return results.ToList();
            }

            private DiscoveredPage NormalizeUrlToPage(string rawUrl)
            {
                var uri = new Uri(rawUrl);
                var path = uri.AbsolutePath.Trim('/');
                if (string.IsNullOrWhiteSpace(path))
                {
                    return new DiscoveredPage { Url = rawUrl, Slug = "/", Title = "Home" };
                }

                var segments = path.Split('/');
                var rawSlug = segments.Last();

                // Convert "plywood-grades" -> "plywood grades" -> "Plywood Grades"
                var slug = rawSlug.Replace("-", " ").Replace("_", " ");
                var textInfo = CultureInfo.CurrentCulture.TextInfo;
                var title = textInfo.ToTitleCase(slug);

                return new DiscoveredPage
                {
                    Url = rawUrl,
                    Slug = rawSlug,
                    Title = title
                };
            }
        }
}
