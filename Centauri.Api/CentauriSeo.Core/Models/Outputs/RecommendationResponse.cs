using CentauriSeo.Core.Models.Output;

namespace CentauriSeo.Core.Models.Outputs
{
    public class RecommendationResponse
    {
        public List<Recommendation> Recommendations { get; set; } = new List<Recommendation>();
        public string Status { get; set; } = "Success";
    }
}
