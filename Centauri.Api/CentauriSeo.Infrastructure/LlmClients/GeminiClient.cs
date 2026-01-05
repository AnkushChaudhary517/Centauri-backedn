using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using CentauriSeo.Infrastructure.LlmDtos;
using CentauriSeo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CentauriSeo.Infrastructure.LlmClients;

public class GeminiClient
{
    private readonly HttpClient _http;
    private readonly ILlmCacheService _cache;
    private readonly IConfiguration _config;

    public GeminiClient(HttpClient http, ILlmCacheService cache, IConfiguration config) => (_http, _cache, _config) = (http, cache, config);

    public async Task<string> GenerateAsync(string input, string systemRequirement)
    {
        var payload = JsonSerializer.Serialize(new { input });

        var systemInstruction = $"use this document for reference. Document : {systemRequirement}.\n Dont invent any new informativeType and voicetype and Structure .... i am providing you the list of values.... anything else will be Uncertain. even with such information you have already provided wrong values...Return a JSON array where each element is an object with properties: " +
                              "\"SentenceId\" (string), \"InformativeType\" (one of Fact|Claim|Definition|Opinion|Prediction|Statistic|Observation|Suggestion|Question|Transition|Filler|Uncertain), " +
                              "\"ClaimsCitation\" (boolean).Voicetype must be either \"Active\" or \"Passive\".  Default VoiceType is \"Active\". Structure must be one of these  Simple|Compound|Complex|CompoundComplex|Fragment .  Default Structure is Simple. If a sentence does not clearly fit a category, you MUST use 'Uncertain'. Do not invent new types. ONLY return the JSON array in the assistant response. The InformativeType must be one of the given values , if its not any of them then it should be Uncertain.Why the hell did you add a wrong value in InformativeType and VoiceType..... never ever ever add any value except from the list";
        var modelName = "gemini-1.5-flash";
        var key = _config["GeminiApiKey"];
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={key}";

        // 2. Gemini Specific JSON Body (Contents/Parts format)
        // 3. Build Gemini-Specific Request Body
        var requestBody = new
        {
            contents = new[]
                    {
                        new { parts = new[] { new { text =$"Context: {systemInstruction}, paylod fo sentence tagging: {payload}" } } }
                    }
        };

        string jsonPayload = JsonSerializer.Serialize(requestBody);
        var requestContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        // 3. Make the call
        try
        {
            using var client = new HttpClient();
            var response = await client.PostAsync(url, requestContent);
            var content = await response.Content.ReadAsStringAsync();

            return content;
        }
        catch(Exception ex)
        {

        }

        return null;


    }

    // NEW: Tag sentences (returns GeminiSentenceTag list). Uses cache if available, falls back to deterministic detectors.
    // Now processes sentences in batches of `batchSize` and aggregates results.
    public async Task<IReadOnlyList<GeminiSentenceTag>> TagSentencesAsync(IEnumerable<Sentence> sentences, string systemRequirement)
    {
        var sentenceList = sentences.ToList();
        var fullPayload = JsonSerializer.Serialize(new { sentences = sentenceList.Select(s => new { id = s.Id, text = s.Text }) });
        var provider = "gemini:tagging";
        var fullKey = _cache.ComputeRequestKey(fullPayload, provider);

        // Try full cached result first
        var fullCached = await _cache.GetAsync(fullKey);
        if (fullCached != null)
        {
            try
            {
                var parsedFull = JsonSerializer.Deserialize<List<GeminiSentenceTag>>(fullCached, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsedFull != null && parsedFull.Count == sentenceList.Count) return parsedFull;
            }
            catch { /* ignore parse errors and proceed to batch calls */ }
        }

        var results = new List<GeminiSentenceTag>();

        const int batchSize = 20;
        for (int offset = 0; offset < sentenceList.Count; offset += batchSize)
        {
            var chunk = sentenceList.Skip(offset).Take(batchSize).ToList();
            var chunkPayload = JsonSerializer.Serialize(new { sentences = chunk.Select(s => new { id = s.Id, text = s.Text }) });
            var chunkKey = _cache.ComputeRequestKey(chunkPayload, provider + ":chunk");

            // Try cached chunk
            var chunkCached = await _cache.GetAsync(chunkKey);
            if (chunkCached != null)
            {
                try
                {
                    var parsedChunk = JsonSerializer.Deserialize<List<GeminiSentenceTag>>(chunkCached, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (parsedChunk != null && parsedChunk.Count == chunk.Count)
                    {
                        // Ensure SentenceId mapping: if parsed contains ids, honor them; otherwise map by position
                        for (int i = 0; i < parsedChunk.Count; i++)
                        {
                            var p = parsedChunk[i];
                            var mappedId = string.IsNullOrWhiteSpace(p.SentenceId) ? chunk[i].Id : p.SentenceId;
                            results.Add(new GeminiSentenceTag
                            {
                                SentenceId = mappedId,
                                Structure = p.Structure,
                                Voice = p.Voice,
                                InformativeType = p.InformativeType,
                                ClaimsCitation = p.ClaimsCitation
                            });
                        }
                        continue;
                    }
                }
                catch { /* ignore parse errors and continue to API */ }
            }

            // Call Gemini API for this chunk
            string apiResponse = null;
            try
            {
                apiResponse = await GenerateAsync(string.Join("\n", chunk.Select(s => s.Text)), systemRequirement);
            }
            catch
            {
                apiResponse = null;
            }

            var parsedSuccessfully = false;
            if (!string.IsNullOrWhiteSpace(apiResponse))
            {
                try
                {
                    // try parse as a JSON array of GeminiSentenceTag (may include SentenceId)
                    var parsed = JsonSerializer.Deserialize<List<GeminiSentenceTag>>(apiResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (parsed != null && parsed.Count == chunk.Count)
                    {
                        // normalized mapping to ensure SentenceId correctness
                        for (int i = 0; i < parsed.Count; i++)
                        {
                            var p = parsed[i];
                            var mappedId = string.IsNullOrWhiteSpace(p.SentenceId) ? chunk[i].Id : p.SentenceId;
                            results.Add(new GeminiSentenceTag
                            {
                                SentenceId = mappedId,
                                Structure = p.Structure,
                                Voice = p.Voice,
                                InformativeType = p.InformativeType,
                                ClaimsCitation = p.ClaimsCitation
                            });
                        }

                        // cache the chunk response
                        try { await _cache.SaveAsync(chunkKey, chunkPayload, apiResponse, provider + ":chunk"); } catch { }
                        parsedSuccessfully = true;
                    }
                    else
                    {
                        // If parsed length different, attempt to parse mapping by SentenceId
                        var parsedMap = JsonSerializer.Deserialize<List<GeminiSentenceTag>>(apiResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (parsedMap != null && parsedMap.Count > 0)
                        {
                            // try to map by SentenceId field values if present
                            foreach (var p in parsedMap)
                            {
                                if (!string.IsNullOrWhiteSpace(p.SentenceId))
                                {
                                    results.Add(new GeminiSentenceTag
                                    {
                                        SentenceId = p.SentenceId,
                                        Structure = p.Structure,
                                        Voice = p.Voice,
                                        InformativeType = p.InformativeType,
                                        ClaimsCitation = p.ClaimsCitation
                                    });
                                }
                            }

                            // if we now have entries for this chunk (by id), consider successful
                            if (results.Count >= offset + parsedMap.Count) // loose check
                            {
                                try { await _cache.SaveAsync(chunkKey, chunkPayload, apiResponse, provider + ":chunk"); } catch { }
                                parsedSuccessfully = true;
                            }
                        }
                    }
                }
                catch
                {
                    parsedSuccessfully = false;
                }
            }

            if (!parsedSuccessfully)
            {
                // fall back to deterministic tagging for this chunk
                foreach (var s in chunk)
                {
                    results.Add(new GeminiSentenceTag
                    {
                        SentenceId = s.Id,
                        Structure = StructureDetector.Detect(s.Text),
                        Voice = VoiceDetector.Detect(s.Text),
                        InformativeType = InformativeTypeDetector.Detect(s.Text),
                        ClaimsCitation = CitationDetector.HasCitation(s.Text)
                    });
                }
            }
        }

        // Cache aggregated final result (full payload) for faster subsequent calls
        try
        {
            if(results != null && results.Count > 0)
            {
                var finalJson = JsonSerializer.Serialize(results);
                if (!string.IsNullOrWhiteSpace(finalJson))
                    await _cache.SaveAsync(fullKey, fullPayload, finalJson, provider);
            }   

        }
        catch { /* ignore cache errors */ }

        return results;
    }
}
