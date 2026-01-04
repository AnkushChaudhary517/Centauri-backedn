using System.Text.RegularExpressions;

namespace CentauriSeo.Core.Models.Utilities
{
    public static class GrammarDetector
    {
        public static bool IsCorrect(string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence)) return false;

            //if (!sentence.EndsWith('.') &&
            //    !sentence.EndsWith('?') &&
            //    !sentence.EndsWith('!'))
            //    return false;

            if (!Regex.IsMatch(sentence.Trim(), @"^[A-Z]"))
                return false;

            return true;
        }
    }

}
