using CentauriSeo.Application.Pipeline;
using CentauriSeo.Core.Models.Input;
using Microsoft.AspNetCore.Mvc;

namespace Centauri_Api.Controllers
{

    [ApiController]
    [Route("api/analyze")]
    public class AnalysisController : ControllerBase
    {
        [HttpPost]
        public IActionResult Analyze([FromBody] SeoRequest request)
        {
            if (request.Article == null || string.IsNullOrWhiteSpace(request.Article.Raw))
            {
                return BadRequest(new
                {
                    status = "failed",
                    missing_inputs = new[] { "article" }
                });
            }

            var sentences = Phase0_InputParser.Parse(request.Article);

            return Ok(new
            {
                status = "success",
                sentence_count = sentences.Count,
                message = "Pipeline executed (LLM calls stubbed for safety)."
            });
        }
    }

}
