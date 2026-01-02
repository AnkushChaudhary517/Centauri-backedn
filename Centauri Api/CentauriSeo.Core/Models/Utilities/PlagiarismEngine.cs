using System.Security.Cryptography;
using System.Text;

namespace CentauriSeo.Core.Models.Utilities
{

    public static class PlagiarismEngine
    {
        public static bool IsPlagiarized(string sentence)
        {
            var hash = ComputeHash(sentence);
            return KnownHashes.Contains(hash);
        }

        private static readonly HashSet<string> KnownHashes = new(); // extensible

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(bytes);
        }
    }

}
