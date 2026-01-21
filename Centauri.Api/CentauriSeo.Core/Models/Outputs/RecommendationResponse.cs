using CentauriSeo.Core.Models.Output;

namespace CentauriSeo.Core.Models.Outputs
{
    public class RecommendationResponseDTO
    {
        public List<RecommendationsResponse> Recommendations { get; set; } = new List<RecommendationsResponse>();
        public string Status { get; set; } = "Success";
        public string RequestId { get; set; } = Guid.NewGuid().ToString();
    }
}
