using System.Text.Json.Serialization;

namespace CentauriSeo.Infrastructure.LlmClients
{
    public class SentenceStrengthResponse
    {
        [JsonPropertyName("sentence")]
        public string Sentence { get; set; }
        [JsonPropertyName("strength")]
        public string Strength { get; set; }
        [JsonPropertyName("reason")]
        public string Reason { get; set; }
    }
    public class SentenceStrengthResponseWrapper
    {
        [JsonPropertyName("results")]
        public List<SentenceStrengthResponse> Results { get; set; }
    }
}