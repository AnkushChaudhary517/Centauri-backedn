using CentauriSeo.Core.Models.Enums;

namespace CentauriSeo.Core.Models.Utilities
{

    public static class InformativeTypeDetector
    {
        public static InformativeType Detect(string s)
        {
            var lower = s.ToLower();

            if (s.EndsWith("?")) return InformativeType.Question;
            if (lower.StartsWith("in this section") || lower.StartsWith("now that"))
                return InformativeType.Transition;

            if (lower.Contains("we believe") || lower.Contains("i think"))
                return InformativeType.Opinion;

            if (lower.Contains("will ") || lower.Contains("may "))
                return InformativeType.Prediction;

            if (s.Any(char.IsDigit))
                return InformativeType.Statistic;

            if (lower.StartsWith("enable ") || lower.StartsWith("use "))
                return InformativeType.Suggestion;

            return InformativeType.Claim;
        }
    }

}
