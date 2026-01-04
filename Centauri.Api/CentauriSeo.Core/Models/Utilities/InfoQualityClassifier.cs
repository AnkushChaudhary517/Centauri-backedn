
using CentauriSeo.Core.Models.Enums;

namespace CentauriSeo.Core.Models.Utilities
{

    public static class InfoQualityClassifier
    {
        public static InfoQuality Classify(string s)
        {
            if (s.Any(char.IsDigit)) return InfoQuality.PartiallyKnown;
            if (s.Length > 180) return InfoQuality.Derived;
            if (s.Contains("our internal") || s.Contains("we found"))
                return InfoQuality.Unique;

            return InfoQuality.WellKnown;
        }
    }

}
