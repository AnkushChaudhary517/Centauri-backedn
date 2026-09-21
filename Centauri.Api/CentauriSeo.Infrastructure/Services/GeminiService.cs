using Amazon.DynamoDBv2.Model;
using CentauriSeo.Core.Models;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CentauriSeo.Infrastructure.Services
{
    public interface IGeminiService
    {
        Task<KeywordAnalysisResponse> AnalyzeKeywordAsync(KeywordAnalysisRequest request, SitemapResult sitemap);
        Task<OutlineResult> GenerateOutlineAsync(OutlineRequest request);
        Task<ArticleResult> GenerateArticleAsync(ArticleRequest request);
    }

    public class GeminiService : IGeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GeminiService> _logger;
        private readonly string _projectId;
        private readonly string _location;

        public GeminiService(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _projectId = configuration["Gemini:ProjectId"]
            ?? System.Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
            ?? System.Environment.GetEnvironmentVariable("GCLOUD_PROJECT")
            ?? "gen-lang-client-0445687823";
            _location = configuration["Gemini:Location"] ?? "asia-south1";
        }

        private async Task<string?> GetAccessTokenAsync()
        {
            try
            {
                var credential = await GoogleCredential.GetApplicationDefaultAsync();
                if (credential.IsCreateScopedRequired)
                {
                    credential = credential.CreateScoped("https://www.googleapis.com/auth/cloud-platform");
                }

                return await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GEMINI_AUTHENTICATION_FAILURE: Failed to obtain ADC token.");
                return null;
            }
        }

        private async Task<string> CallGeminiWithSearchGroundingAsync(string prompt)
        {
            var token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException("Could not obtain a valid OAuth access token using Application Default Credentials.");
            }

            // Using Vertex AI REST Endpoint with Google Cloud Project and Location
            var requestUri = $"https://{_location}-aiplatform.googleapis.com/v1/projects/{_projectId}/locations/{_location}/publishers/google/models/gemini-2.5-flash:generateContent";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                // Enabling Google Search Grounding Tool
                tools = new[]
                {
                    new { googleSearch = new { } }
                }
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(jsonResponse);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return text ?? string.Empty;
        }

        public async Task<KeywordAnalysisResponse> AnalyzeKeywordAsync(KeywordAnalysisRequest request, SitemapResult sitemap)
        {
            var prompt = $@"
You are an experienced SEO strategist and keyword research expert.

Analyze the following keyword and target website.

Keyword: ""{request.PrimaryKeyword}""
Target Region: ""{request.TargetRegion}""
Website: ""{request.TargetUrl}""

The following sitemap pages are already indexed and available for internal linking:

{JsonSerializer.Serialize(sitemap.Pages)}

Return ONLY a valid JSON object.

Rules:
- Do NOT wrap the response inside ```json or markdown.
- Do NOT include comments.
- Do NOT include explanations outside the JSON.
- Every property name must be enclosed in double quotes.
- The JSON must be directly deserializable into C# classes.
- Numeric values must be numbers, NOT strings.
- If an exact value cannot be determined, provide your best realistic estimate.
- Do not return null unless absolutely necessary.
- All arrays must always exist, even if empty.

Return JSON in exactly the following structure:

{{
  ""keyword"": """",
  ""targetRegion"": """",

  ""analysis"": {{

    ""searchIntent"": [
      {{
        ""type"": """",
        ""description"": """",
        ""priority"": 1
      }}
    ],

    ""monthlySearchVolume"": {{
      ""value"": 0,
      ""confidence"": """",
      ""sourceNote"": """"
    }},

    ""averageCpc"": {{
      ""currency"": ""INR"",
      ""value"": 0,
      ""confidence"": """",
      ""sourceNote"": """"
    }},

    ""keywordDifficulty"": {{
      ""score"": 0,
      ""level"": """",
      ""reason"": """"
    }},

    ""trafficPotential"": {{
      ""monthlyClicks"": 0,
      ""assumptions"": """"
    }},

    ""keywordClusters"": [
      {{
        ""clusterName"": """",
        ""intent"": """",
        ""keywords"": [
          """"
        ]
      }}
    ],

    ""rankingTargets"": [
      {{
        ""keyword"": """",
        ""intent"": """",
        ""difficulty"": 0
      }}
    ],

    ""contentGapAnalysis"": {{
      ""status"": """",
      ""summary"": """",
      ""missingTopics"": [
        """"
      ]
    }},

    ""indexabilityMetrics"": {{
      ""topicalRelevance"": 0,
      ""authority"": 0,
      ""internalLinking"": 0,
      ""reason"": """"
    }}

  }}

}}

Important:
Only return the JSON object.
Do not return markdown.
Do not return any additional text before or after the JSON.
";

            var aiResponse = await CallGeminiWithSearchGroundingAsync(prompt);
            string cleaned = Regex.Replace(
    aiResponse,
    @"^\s*```(?:json)?\s*|\s*```\s*$",
    "",
    RegexOptions.IgnoreCase | RegexOptions.Singleline
).Trim();
            var analysisResult = JsonSerializer.Deserialize<KeywordAnalysisResponse>(cleaned);
            return analysisResult;
        }

        public async Task<OutlineResult> GenerateOutlineAsync(OutlineRequest request)
        {
            var availableUrlsPrompt = JsonSerializer.Serialize(request.SelectedTargetPages);
            var prompt = $@"
Generate an H2/H3 article outline based on key analysis data:
Keyword: {request.AnalysisData.PrimaryKeyword}
Target Audience Notes: {request.TargetAudienceNotes}

Available Reconciled URLs for Internal Link Mapping:
{availableUrlsPrompt}

Return a structured outline where sections map specific internal URLs to relevant headings for strategic anchor-text placement.";

            var aiResponse = await CallGeminiWithSearchGroundingAsync(prompt);

            return new OutlineResult
            {
                Title = $"Comprehensive Guide to {request.AnalysisData.PrimaryKeyword}",
                Sections = request.SelectedTargetPages.Select((page, index) => new OutlineSection
                {
                    Heading = $"Understanding {page.Title}",
                    Level = index % 2 == 0 ? "H2" : "H3",
                    BulletPoints = new List<string> { "Overview and basics", "Key technical specs", "Best use cases" },
                    SuggestedInternalLinkUrl = page.Url,
                    SuggestedAnchorText = page.Title
                }).ToList()
            };
        }

        public async Task<ArticleResult> GenerateArticleAsync(ArticleRequest request)
        {
            var prompt = $@"
Synthesize a full SEO publication-ready article in Markdown format based on this outline:
{JsonSerializer.Serialize(request.Outline)}

Primary Keyword: {request.PrimaryKeyword}
LSI Keywords: {string.Join(", ", request.LsiKeywords)}

Instructions:
1. Contextually embed internal links using the mapping: {JsonSerializer.Serialize(request.InterlinkingMapping)}.
2. Provide an SEO Meta Title and Meta Description.
3. Provide valid JSON-LD FAQ Schema markup at the end.
";

            var aiResponse = await CallGeminiWithSearchGroundingAsync(prompt);

            return new ArticleResult
            {
                MetaTitle = $"{request.PrimaryKeyword} | Complete Professional Guide",
                MetaDescription = $"Learn everything about {request.PrimaryKeyword}. Detailed operational logic and step-by-step breakdown.",
                ContentMarkdown = aiResponse,
                FaqSchemaJsonLd = "{\"@context\":\"https://schema.org\",\"@type\":\"FAQPage\",\"mainEntity\":[]}"
            };
        }
    }
}