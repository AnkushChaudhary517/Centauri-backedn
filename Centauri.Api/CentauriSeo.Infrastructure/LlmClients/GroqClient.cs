using CentauriSeo.Core.Models.Output;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using CentauriSeo.Infrastructure.LlmDtos;
using CentauriSeo.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace CentauriSeo.Infrastructure.LlmClients;

public class GroqClient
{
    private readonly HttpClient _http;
    private readonly ILlmCacheService _cache;
    private readonly string _apiKey;
    private readonly Uri _baseUri;

    public GroqClient(HttpClient http, ILlmCacheService cache)
    {
        _http = http;
        _cache = cache;
        _apiKey = _http.DefaultRequestHeaders.Authorization?.Parameter ?? string.Empty;
        _baseUri = _http.BaseAddress ?? new Uri("https://api.groq.com");
    }

    // Low-level analyze (kept for compatibility)
    // Sends an OpenAI-compatible chat/completions payload and returns assistant content (machine-parsable JSON expected).
    public async Task<string> AnalyzeAsync(string payload, string systemRequirement)
    {
        var provider = "groq:analyze";

        // Use a short-lived HttpClient with DNS re-resolve as you had before, but reuse request format below.
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2) // Re-resolve DNS periodically
        };
        using var client = new HttpClient(handler) { BaseAddress = _baseUri };

        if (!string.IsNullOrWhiteSpace(_apiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        // Build chat/completions request body (OpenAI-compatible)
        var systemInstruction = $"use this document for reference. Document : {systemRequirement}.\n Dont invent any new informativeType.... i am providing you the list of values.... anything else will be Uncertain. even with such information you have already provided wrong values...Return a JSON array where each element is an object with properties: " +
                                "\"SentenceId\" (string), \"InformativeType\" (one of Fact|Claim|Definition|Opinion|Prediction|Statistic|Observation|Suggestion|Question|Transition|Filler|Uncertain), " +
                                "\"ClaimsCitation\" (boolean).If a sentence does not clearly fit a category, you MUST use 'Uncertain'. Do not invent new types. ONLY return the JSON array in the assistant response. The InformativeType must be one of the given values , if its not any of them then it should be Uncertain.Why the hell did you add a wrong value in InformativeType..... never ever ever add any value except from the list";


        var prompt = "Response must have same number of sentences as in the request. Default value of InformativeType is Uncertain."+SentenceTaggingPrompts.GroqTagPrompt+"Do not give me that value.Default Value of InformativeType is Uncertain.And Why are you giving wrong value in InformativeType? There is no Example in InformativeType. Use only the provided values." + $"<text>\r\n{payload}\r\n</text>";
        // Compose user content with indexed sentence IDs so response includes stable IDs
        //var userContent = string.Join("\n", sentenceList.Select((t, i) => $"S{i + 1}: {t}"));
        //prompt = "Why are you returning only 4 sentences in json array. I want all the sentences in from json.Response must be json array. Recheck the json format before sending the response. Default value of InformativeType is Uncertain.This document combines an instruction specification for an AI acting as an Expert SEO Content Editor and Linguistic Analyst with a complete SEO analysis report and article focused on the keyword \"fractional CFO cost\". The AI is required to perform parallel sentence tagging by reading XML input containing sentence IDs and tagging each sentence using predefined Level 1 linguistic taxonomies based purely on linguistic form without verifying facts. These taxonomies include FunctionalType, Structure, Voice, InformativeType (defaulting to Uncertain), and InfoQuality (which must never be Uncertain), along with binary flags for claims citation, grammatical correctness, and pronoun usage. The output must strictly be a raw JSON array with no markdown or explanatory text. The document also includes an SEO analysis report with scoring across SEO, AI indexing, authority, plagiarism, and readability, followed by a medium-priority improvement action plan addressing capitalization, punctuation, passive voice, informal language, formatting, and transition issues with original and corrected examples. The article content explains how much a fractional CFO costs in 2025 for startup founders, covering pricing models, cost comparisons, included and excluded services, factors influencing cost, when to hire, how to choose a CFO, red flags, ROI evaluation, and FAQs, with a primarily informational intent and commercial evaluation elements, designed to support AI-driven SEO scoring, content quality analysis, and structured linguistic classification.";

        // prompt = "why the hell are you giving me code? i want json response.Default value of InformativeType is Uncertain. Role: Expert SEO Content Editor & Linguistic Analyst. Task: Phase 1 – Parallel Sentence Tagging: read the XML, map each sentence to its ID (S1, S2, …), and tag it using the Level 1 taxonomies. Taxonomies: FunctionalType – Declarative, Interrogative, Imperative, Exclamatory; Structure – Simple, Compound, Complex, CompoundComplex, Fragment; Voice – Active (default), Passive; InformativeType – Fact, Statistic, Definition, Claim, Observation, Opinion, Prediction, Suggestion, Question, Transition, Filler, Uncertain; InfoQuality – WellKnown, PartiallyKnown, Derived, Unique, False (never Uncertain). Binary Flags: ClaimsCitation – true if the sentence contains a first-person source, a third-person citation, or a hyperlink; IsGrammaticallyCorrect – false on any typo, tense shift, or punctuation error; HasPronoun – true if any personal, demonstrative, or relative pronoun appears. Constraints: Do not verify facts; tag purely on linguistic form; return only a raw JSON array with no markdown and no intro or outro; InfoQuality is never Uncertain and must never be returned as Uncertain; use only the provided enum values; do not invent values; do not give any example values for InformativeType; default value of InformativeType is Uncertain.";
        var requestBody = new
        {
            model = "llama-3.1-8b-instant",
            messages = new[]
            {
                //new { role = "system", content = prompt },
                new { role = "user", content =SentenceTaggingPrompts.GeminiSentenceTagPrompt + $"<text>\r\n{payload}\r\n</text>"
    }
            },
            temperature = 0.0,
            max_tokens = 32000
        };
        using var stringContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        string rawResponse = null;
        try
        {
            var res = await client.PostAsync("/openai/v1/chat/completions", stringContent);
            rawResponse = await res.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException)
        {
            //// Network/DNS error — fallback to deterministic local tagging formatted as JSON
            //var fallbackTags = sentenceList.Select((t, i) => new PerplexitySentenceTag
            //{
            //    SentenceId = $"S{i + 1}",
            //    InformativeType = InformativeTypeDetector.Detect(t),
            //    ClaimsCitation = CitationDetector.HasCitation(t)
            //}).ToList();

            //var fallbackJson = JsonSerializer.Serialize(fallbackTags);
            //try
            //{
            //    await _cache.SaveAsync(key, payload, fallbackJson, provider + ":fallback");

            //}
            //catch
            //{

            //}
            //return fallbackJson;
        }

        // Extract assistant content from common chat/completions envelope:
        // { "choices":[ { "message": { "content": "..." } } ] } or { "choices":[ { "text": "..." } ] }
        string assistantContent = rawResponse;
        try
        {
            using var doc = JsonDocument.Parse(rawResponse);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                {
                    assistantContent = content.GetString() ?? rawResponse;
                }
                else if (first.TryGetProperty("text", out var text))
                {
                    assistantContent = text.GetString() ?? rawResponse;
                }
                else if (first.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var dcontent))
                {
                    assistantContent = dcontent.GetString() ?? rawResponse;
                }
            }
        }
        catch(Exception)
        {
            assistantContent = rawResponse; // keep raw if parsing fails
        }
        return assistantContent;
    }

    public async Task<IReadOnlyList<PerplexitySentenceTag>> TagArticleAsync(string article)
    {
        var provider = "groq:tagging";
        var key = _cache.ComputeRequestKey(article, provider);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters ={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)}
        };

        var cached = await _cache.GetAsync(key);
        if (cached != null)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<PerplexitySentenceTag>>(cached, options);
                if (parsed != null) return parsed;
            }
            catch { /* ignore parse errors and continue */ }
        }

        try
        {
            var apiResponse = await AnalyzeAsync(article, string.Empty);
            if (!string.IsNullOrWhiteSpace(apiResponse))
            {
                string json = apiResponse
                    .Replace("```json", "")
                    .Replace("```", "")
                    .Trim();
                var start = json.IndexOf("[");
                var end = json.LastIndexOf("]");
                string inner = "["+json.Substring(start + 1, end - start - 1)+"]";
                json = inner;
                var parsed = JsonSerializer.Deserialize<List<PerplexitySentenceTag>>(json, options);
                if (parsed != null)
                {
                    await _cache.SaveAsync(key, JsonSerializer.Serialize(parsed));
                    return parsed;
                }
            }

        }
        catch(Exception ex)
        {
        }

        return new List<PerplexitySentenceTag>();

    }

    // Tag sentences for Level-1 (returns PerplexitySentenceTag dto for compatibility)
    // Added batching: process sentences in batches and then combine results into a single list.

    public async Task<IReadOnlyList<PerplexitySentenceTag>> TagSentencesAsync(string userContent, string systemRequirement)
    {
        return await TagArticleAsync(userContent);
    }
    public async Task<IReadOnlyList<PerplexitySentenceTag>> TagSentencesAsync(IEnumerable<Sentence> sentences, string systemRequirement)
    {
        var sentenceList = sentences.ToList();
        var payload = JsonSerializer.Serialize(new { sentences = sentenceList.Select(s => new { id = s.Id, text = s.Text }) });
        return await TagArticleAsync(payload);
        //    var provider = "groq:tagging";
        //    var key = _cache.ComputeRequestKey(payload, provider);

        //    var cached = await _cache.GetAsync(key);
        //    if (cached != null)
        //    {
        //        try
        //        {
        //            var parsed = JsonSerializer.Deserialize<List<PerplexitySentenceTag>>(cached, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        //            if (parsed != null && parsed.Count == sentenceList.Count) return parsed;
        //        }
        //        catch { /* ignore parse errors and continue */ }
        //    }

        //    var results = new List<PerplexitySentenceTag>();

        //    // Batch size - tune as needed
        //    const int batchSize = 20;

        //    for (int offset = 0; offset < sentenceList.Count; offset += batchSize)
        //    {
        //        var chunk = sentenceList.Skip(offset).Take(batchSize).ToList();
        //        string apiResponse;

        //        try
        //        {
        //            apiResponse = await AnalyzeAsync(payload, systemRequirement);
        //        }
        //        catch
        //        {
        //            apiResponse = null!;
        //        }

        //        bool parsedChunk = false;

        //        if (!string.IsNullOrWhiteSpace(apiResponse))
        //        {
        //            try
        //            {
        //                var options = new JsonSerializerOptions
        //                {
        //                    PropertyNameCaseInsensitive = true,
        //                    Converters =
        //{
        //    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        //    // or omit JsonNamingPolicy if enum names match exactly
        //}
        //                };
        //                var parsed = JsonSerializer.Deserialize<List<PerplexitySentenceTag>>(apiResponse,options);
        //                if (parsed != null)
        //                {
        //                    results.AddRange(parsed);
        //                    // Map parsed entries to the original sentence IDs by position
        //                    //for (int i = 0; i < parsed.Count; i++)
        //                    //{
        //                    //    var p = parsed[i];
        //                    //    results.Add(new PerplexitySentenceTag
        //                    //    {
        //                    //        SentenceId = chunk[i].Id,
        //                    //        InformativeType = p.InformativeType,
        //                    //        ClaimsCitation = p.ClaimsCitation
        //                    //    });
        //                    //}

        //                    parsedChunk = true;
        //                }
        //            }
        //            catch
        //            {
        //                parsedChunk = false;
        //            }
        //        }

        //        if (!parsedChunk)
        //        {
        //            // Deterministic fallback for this chunk
        //            foreach (var s in chunk)
        //            {
        //                results.Add(new PerplexitySentenceTag
        //                {
        //                    SentenceId = s.Id,
        //                    InformativeType = InformativeTypeDetector.Detect(s.Text),
        //                    ClaimsCitation = CitationDetector.HasCitation(s.Text)
        //                });
        //            }
        //        }
        //    }

        //    // Cache the aggregated result for the full payload
        //    try
        //    {
        //        var finalJson = JsonSerializer.Serialize(results);
        //        await _cache.SaveAsync(key, payload, finalJson, provider);
        //    }
        //    catch
        //    {
        //        // ignore cache errors
        //    }

        //    return results;
    }
}