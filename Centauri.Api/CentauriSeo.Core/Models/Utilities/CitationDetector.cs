namespace CentauriSeo.Core.Models.Utilities
{

    public static class CitationDetector
    {
        public static bool HasCitation(string s)
        {
            var lower = s.ToLower();

            return lower.Contains("according to")
                || lower.Contains("as per")
                || lower.Contains("we observed")
                || lower.Contains("we noticed")
                || lower.Contains("http://")
                || lower.Contains("https://");
        }
    }

}
