using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Utilities
{
    public static class CacheHelper
    {

        public static async Task<string> GetGeminiTagGenerationCacheKey(string apiKey)
        {
            var client = new HttpClient();

            var cacheRequest = new
            {
                model = "models/gemini-2.5-flash",
                system_instruction = new
                {
                    parts = new[]
        {
            new { text = SentenceTaggingPrompts.GeminiSentenceTagPrompt }
        }
                },
                // Contents can be empty if you only want to cache the system instruction,
                // or it must contain objects with role "user" or "model".
                contents = new[] {
        new {
            role = "user",
            parts = new[] { new { text = " " } } // Dummy user start if needed
        }
    },
            };

            var response = await client.PostAsJsonAsync(
                $"https://generativelanguage.googleapis.com/v1beta/cachedContents?key={apiKey}",
                cacheRequest
            );

            var json = await response.Content.ReadAsStringAsync();
            return json;
        }
    }
}
