using CentauriSeo.Core.Models.Output;

namespace CentauriSeo.Core.Models.Outputs
{
    public class RecommendationResponseDTO
    {
        public RecommendationsResponse Recommendations { get; set; } = new RecommendationsResponse();
        public string Status { get; set; } = "Completed";
        public string RequestId { get; set; } = Guid.NewGuid().ToString();
    }
}
