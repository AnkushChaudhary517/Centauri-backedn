using CentauriSeo.Core.Models;
using CentauriSeo.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Centauri_Api.Controllers
{
        [ApiController]
        [Route("api/[controller]")]
        public class SitemapController : ControllerBase
        {
            private readonly ISitemapService _sitemapService;

            public SitemapController(ISitemapService sitemapService)
            {
                _sitemapService = sitemapService;
            }

            [HttpPost]
            public async Task<IActionResult> GetSitemap([FromBody] SitemapRequest request)
            {
                if (string.IsNullOrWhiteSpace(request.Url))
                {
                    return BadRequest(new { error = "Target URL is required." });
                }

                var result = await _sitemapService.DiscoverSitemapAsync(request.Url);
                return Ok(result);
            }
        }
}
