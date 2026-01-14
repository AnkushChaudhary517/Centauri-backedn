namespace CentauriSeo.Application.Scoring
{
    public class ContentData
    {
        public string H1 { get; set; }
        public string MetaTitle { get; set; }
        public string MetaDescription { get; set; }
        public string UrlSlug { get; set; }
        public List<string> HeadersH2H3 { get; set; } = new();
        public string RawBodyText { get; set; }
    }
}