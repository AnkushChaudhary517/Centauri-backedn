using CentauriSeo.Infrastructure.Services;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using CentauriSeo.Infrastructure.LlmDtos;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;

namespace CentauriSeo.Infrastructure.LlmClients;

public class PerplexityClient
{
    private readonly HttpClient _http;
    private readonly ILlmCacheService _cache;

    public PerplexityClient(HttpClient http, ILlmCacheService cache) => (_http, _cache) = (http, cache);

    // Existing low-level call (keeps backwards compatibility)
    public async Task<string> AnalyzeAsync(IEnumerable<string> sentences)
    {
        var payload = JsonSerializer.Serialize(new { sentences });
        var provider = "perplexity:sonar-pro";
        var key = _cache.ComputeRequestKey(payload, provider);

        var cached = await _cache.GetAsync(key);
        if (cached != null) return cached;

        var res = await _http.PostAsJsonAsync("/chat/completions", new
        {
            model = "sonar-pro",
            messages = new[] { new { role = "user", content = sentences } }
        });

        var content = await res.Content.ReadAsStringAsync();
        await _cache.SaveAsync(key, payload, content, provider);
        return content;
    }

    // NEW: Tag sentences (returns PerplexitySentenceTag list). Uses cache if available, falls back to deterministic detectors.
    public async Task<IReadOnlyList<PerplexitySentenceTag>> TagSentencesAsync(IEnumerable<Sentence> sentences)
    {
        var sentenceList = sentences.ToList();
        var payload = JsonSerializer.Serialize(new { sentences = sentenceList.Select(s => new { id = s.Id, text = s.Text }) });
        var provider = "perplexity:tagging";
        var key = _cache.ComputeRequestKey(payload, provider);

        var cached = await _cache.GetAsync(key);
        if (cached != null)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<PerplexitySentenceTag>>(cached, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsed != null && parsed.Count == sentenceList.Count) return parsed;
            }
            catch { /* ignore parse errors and proceed to call API or fallback */ }
        }

        // Attempt real API call (if available)
        try
        {
            var apiResponse = await AnalyzeAsync(sentenceList.Select(s => s.Text));
            // Try parse the API response into DTOs (expecting JSON list)
            try
            {
                var parsed = JsonSerializer.Deserialize<List<PerplexitySentenceTag>>(apiResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsed != null && parsed.Count == sentenceList.Count)
                {
                    await _cache.SaveAsync(key, payload, apiResponse, provider);
                    return parsed;
                }
            }
            catch { /* fallthrough to deterministic fallback */ }

            // If API returned but could not parse, still cache raw and fall back
            await _cache.SaveAsync(key, payload, apiResponse, provider);
        }
        catch
        {
            // ignore network errors and fallback to deterministic tags
        }

        // Deterministic fallback using local detectors
        var fallback = sentenceList.Select(s => new PerplexitySentenceTag
        {
            SentenceId = s.Id,
            InformativeType = InformativeTypeDetector.Detect(s.Text),
            ClaimsCitation = CitationDetector.HasCitation(s.Text)
        }).ToList();

        // Cache the deterministic JSON form so future runs reuse it
        var fallbackJson = JsonSerializer.Serialize(fallback);
        await _cache.SaveAsync(key, payload, fallbackJson, provider);

        return fallback;
    }
}
