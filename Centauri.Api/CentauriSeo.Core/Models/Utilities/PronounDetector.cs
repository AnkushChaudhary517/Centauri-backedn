namespace CentauriSeo.Core.Models.Utilities
{
    public static class PronounDetector
    {
        private static readonly string[] Pronouns =
        {
        " we ", " you ", " they ", " this ", " that ", " which ", " who ", " it "
    };

        public static bool ContainsPronoun(string s)
        {
            var lower = $" {s.ToLower()} ";
            return Pronouns.Any(p => lower.Contains(p));
        }
    }

}
