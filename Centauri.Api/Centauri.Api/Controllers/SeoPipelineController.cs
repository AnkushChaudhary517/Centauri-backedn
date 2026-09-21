using CentauriSeo.Core.Models;
using CentauriSeo.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Centauri_Api.Controllers
{
        [ApiController]
        [Route("api/v1/[controller]")]
        public class SeoPipelineController : ControllerBase
        {
            private readonly ISitemapService _sitemapService;
            private readonly IGeminiService _geminiService;

            public SeoPipelineController(ISitemapService sitemapService, IGeminiService geminiService)
            {
                _sitemapService = sitemapService;
                _geminiService = geminiService;
            }

            // Step 1: Keyword Analysis & Competitor Audit
            [HttpPost("analyze-keyword")]
            public async Task<IActionResult> AnalyzeKeyword([FromBody] KeywordAnalysisRequest request)
            {
                var sitemap = await _sitemapService.DiscoverSitemapAsync(request.TargetUrl);
                var analysisResult = await _geminiService.AnalyzeKeywordAsync(request, sitemap);
                return Ok(analysisResult);
            }

            // Step 2: Content Outline Generation
            [HttpPost("generate-outline")]
            public async Task<IActionResult> GenerateOutline([FromBody] OutlineRequest request)
            {
                var outlineResult = await _geminiService.GenerateOutlineAsync(request);
                return Ok(outlineResult);
            }

            // Step 3: Full SEO Article & Schema Synthesis
            [HttpPost("generate-article")]
            public async Task<IActionResult> GenerateArticle([FromBody] ArticleRequest request)
            {
                var articleResult = await _geminiService.GenerateArticleAsync(request);
                return Ok(articleResult);
            }
        }
}
