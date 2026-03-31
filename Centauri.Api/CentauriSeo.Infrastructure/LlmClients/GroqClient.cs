using CentauriSeo.Core.Models.Output;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using CentauriSeo.Infrastructure.LlmDtos;
using CentauriSeo.Infrastructure.Logging;
using CentauriSeo.Infrastructure.Services;
using CentauriSeo.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Google.Apis.Requests.BatchRequest;
using static System.Net.Mime.MediaTypeNames;

namespace CentauriSeo.Infrastructure.LlmClients;

public class GroqClient
{
    private readonly HttpClient _http;
    private readonly ILlmCacheService _cache;
    private readonly ILlmCacheManager _cacheManager;
    private readonly string _apiKey;
    private readonly Uri _baseUri;
    private readonly FileLogger _logger;
    private readonly ILlmLogger _llmLogger;
    private readonly AiCallTracker _aiCallTracker;

    public GroqClient(HttpClient http, ILlmCacheService cache, AiCallTracker aiCallTracker, ILlmCacheManager cacheManager, ILogger<LlmLogger> logger)
    {
        _http = http;
        _cache = cache;
        _cacheManager = cacheManager;
        _apiKey = Environment.GetEnvironmentVariable("CentauriGroqApiKey");
        _baseUri = _http.BaseAddress ?? new Uri("https://api.groq.com");
        _logger = new FileLogger();
        _llmLogger = new LlmLogger(logger);
        _aiCallTracker = aiCallTracker;

        _llmLogger.LogInfo("GroqClient initialized successfully");
    }

    public async Task<string> UpdateInformativeType(string userContent)
    {
        string endpoint = "https://api.groq.com/openai/v1/chat/completions";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        var requestBody = new
        {
            model = "llama-3.3-70b-versatile",
            messages = new[] {
            new { role = "system", content = $@"
        Please update the userContent where informative Type is wrong and return the data in same format just update the informative type value

Classify as FACT only if the sentence contains specific data, statistics, or universally verifiable truths. Classify as CLAIM if the sentence makes a promotional statement about a product's capabilities, quality, or internal design intent.

Analyze the article and return ONLY a plain JSON ARRAY of objects. 
Do NOT wrap the response in a ""data"" or ""updatedData"" key. 
Do NOT include any conversational text or markdown blocks (```json).
Format(Array of objects):
      [
        {{
          ""InformativeType"": ""Enum_Value"",
          ""Sentence"": ""The actual sentence text""
        }}
      ]

    Allowed Enum Values:
    - Question, Suggestion, Definition, Fact, Statistic, Claim, Uncertain, Opinion, Filler, Prediction

Default value is Uncertain only if there are geninuly no other value possible." 
            
     },
            new { role = "user", content = userContent }
        },
            response_format = new { type = "json_object" },
            temperature = 0 // Lowest temperature for maximum logic 
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(endpoint, content);
            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);
            var data = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return data;
        }
        catch (Exception ex)
        {
            return null;
        }

    } 
    public async Task<List<string>> GetGroqCategorization(List<string> h2Tags)
    {
        string endpoint = "https://api.groq.com/openai/v1/chat/completions";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        var prompt = $@"
    Mapping Rules:
    1. Definition (Concept Clarification)
    2. Process (How-To)
    3. Problem (Pain-Oriented)
    4. Solution (Recommendation)
    5. Analytical (Insight)
    6. Data (Evidence)
    7. Comparison (Evaluates alternatives)
    8. FAQ (Long-tail/edge cases)
    9. Cost (ROI/Pricing)
    10. Implementation (Setup)

    TASK:
    Categorize each of the H2 tags below. 
    Return ONLY a simple JSON array of strings containing the Category Names in the same order as the input.

    H2 TAGS:
    {string.Join("\n", h2Tags)}

    STRICT OUTPUT FORMAT:
    [""""Category1"""", """"Category2"""", """"Category3""""]
    Allowed values: (Definition|Process|Problem|Solution|Analytical|Data|Comparison|FAQ|Cost|Implementation)
    ";

        var requestBody = new
        {
            model = "llama-3.3-70b-versatile",
            messages = new[] {
            new { role = "system", content = "You are a JSON-only SEO classifier. No preamble. No conversational text." },
            new { role = "user", content = prompt }
        },
            response_format = new { type = "json_object" },
            temperature = 0 // Lowest temperature for maximum logic 
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(endpoint, content);
            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);
            var data =  doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            var res =  JsonSerializer.Deserialize<GroqHeadingsResponse>(data);
            return res.categories;
        }
        catch (Exception ex)
        {
            return null;
        }
    }


    public async Task<List<SentenceStrengthResponse>> GetSentenceStrengths(List<GeminiSentenceTag> sentences)
    {
        var prompt = $@"### ROLE
You are a Source Verification Specialist. Your task is to analyze the 'Source Strength' of any given statement by identifying the PRESENCE or ABSENCE of specific structural markers (Attributions and Anchors).

### EVALUATION LOGIC (STRUCTURAL HIERARCHY)
Apply this logic to ANY document or niche. The examples provided are for illustrative purposes only; use the underlying logic for all technical, financial, or general claims.

1. **STRONG**:
   - **Formula**: [Any Named Entity/Authority OR First-Person/Internal Source] + [At least one Verifiability Anchor].
   - **Anchors include**: Specific years, named reports/studies, official regulation numbers (e.g., ISO, Article X, Form Y), specific sample sizes (n=X), precise timeframes, or external URLs.
   - **Rule**: A name alone is NOT strong. It must have a secondary verifiable detail.

2. **MEDIUM**:
   - **Formula**: [Any Named Entity/Authority OR First-Person Statement] ONLY.
   - **Markers**: Direct attribution to a specific company, person, organization, or ""In our experience/survey,"" but without a specific year or report title.

3. **WEAK**:
   - **Formula**: [Vague/Anonymous Attribution] ONLY.
   - **Markers**: Phrases like ""Experts say,"" ""Studies show,"" ""Research suggests,"" or ""It is widely believed"" without naming the specific entity.

4. **NONE**:
   - **Formula**: [Factual/Numeric Claim] + [Zero Attribution].
   - **Markers**: Any bold claim, statistic, or process step stated as a fact without any source or first-person disclosure.

### CRITICAL INSTRUCTIONS FOR LLAMA-3.3-70B:
- **NON-EXCLUSIVE EXAMPLES**: The examples in the logic are patterns, not an exhaustive list. Treat ANY proper noun (Companies, Agencies, Laws) as a 'Named Authority'.
- **LOGICAL INFERENCE**: If a sentence mentions ""According to [Entity]"", it MUST be at least MEDIUM. Do not return 'NONE' if ANY attribution exists.
- **SOURCE TYPE AGNOSTIC**: This applies to all niches (SEO, Tax, Tech, Health). Do not look for specific keywords from the examples; look for the *structure* of the attribution.

### JSON SCHEMA
{{
  ""results"": [
    {{
      ""sentence"": ""string"",
      ""strength"": ""Strong | Medium | Weak | None"",
      ""found_authority"": ""string (the name found, or 'anonymous')"",
      ""found_anchor"": ""string (the detail/year found, or 'none')"",
      ""reason"": ""Brief explanation of why this strength was assigned.""
    }}
  ]
}}";
        string endpoint = "https://api.groq.com/openai/v1/chat/completions";
        var payload = JsonSerializer.Serialize(sentences.Select(s => s.Sentence).ToList());
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

        var requestBody = new
        {
            model = "llama-3.3-70b-versatile",
            messages = new[]
            {
            new { role = "system", content = prompt },
            new { role = "user", content = payload }
        },
            response_format = new { type = "json_object" },
            temperature = 0
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        try
        {
            var response = await client.PostAsync(endpoint, content);
            var jsonResponse = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(jsonResponse);
            var data = doc.RootElement
                          .GetProperty("choices")[0]
                          .GetProperty("message")
                          .GetProperty("content")
                          .GetString();

            var result = JsonSerializer.Deserialize<SentenceStrengthResponseWrapper>(data);

            return result?.Results;
        }
        catch
        {
            return null;
        }
    }
    public async Task<GroqBulkResponse> GetExpertise(string payload)
    {
        string bulkSystemPrompt = @"
### ROLE
You are an Advanced SEO Semantic Analyst. Your goal is to audit text for E‑E‑A‑T (Experience, Expertise, Authoritativeness) using the linguistic forensic patterns described below.

### MARKER DEFINITIONS & GUARDRAILS

1. **Technical Breadth (T_u)**
   One‑Statement Definition: A unique list of domain‑specific nouns, acronyms, or jargon that defines the semantic boundaries of the niche.
   LLM Guardrail (The ""Stop‑List""): Do NOT count ""Corporate Fluff"" or general business adjectives (e.g., efficient, innovative, scalable, professional).
   Restriction: A term only counts if it is defined in the text **or** appears ≥ 2 times.

2. **Analytical Depth (L_c)**
   One‑Statement Definition: Syntax bridges that explicitly connect a functional premise to a technical consequence.
   LLM Guardrail (The ""Substitution Test""): If you can remove the connector and the logic remains identical, it is not L_c.
   Restriction: Exclude simple list‑making conjunctions (""and"", ""also"").

3. **Empirical Grounding (M_g)**
   One‑Statement Definition: Verifiable, non‑subjective anchors including metrics, legal codes, and specific regulatory bodies.
   LLM Guardrail (The ""Verification Rule""): Every M_g must be categorized as [Metric], [Regulatory/Legal] or [Proper Noun/Body].
   Restriction: Generalities like ""the law"" or ""recent studies"" receive a score of 0.

4. **Practitioner Persona (S_e)**
   One‑Statement Definition: Language that signals ""scar tissue"" or first‑hand navigation of a process through predictive warnings or methodology.
   LLM Guardrail (The ""Intern Test""): If a marketing intern could write the sentence after a 5‑minute search, it is not S_e.
   Restriction: Must be followed by a concrete detail within 1–2 sentences.

5. **Operational Specificity (O_s)**
   One‑Statement Definition: A 100 % executable technical action satisfying the strict 4‑of‑4 checklist.
   LLM Guardrail (The ""Component Mapping""): You must map the sentence to [Trigger] + [Action] + [Object] + [Outcome].
   Restriction: If any one of the four components is missing or vague, O_s = 0.

-----------------------------------------------------------------
### JSON OUTPUT REQUIREMENTS
- **Return ONLY the JSON object** – no introductory text, no markdown fences.
- The object **must have a top‑level property named `results`** that contains an array of the per‑section objects.
- The JSON must conform exactly to this schema:

{
  ""results"": [
    {
      ""sectionId"": ""string"",
      ""T_u_list"": [],          // distinct technical tokens
      ""L_c_count"": 0,
      ""T_u_count"": 0,          // must equal T_u_list.Count
      ""M_g_count"": 0,
      ""S_e_count"": 0,
      ""O_s_count"": 0
    }
  ]
}

-----------------------------------------------------------------
### EXAMPLE OF A VALID RESPONSE

{
  ""results"": [
    {
      ""sectionId"": ""S1"",
      ""T_u_list"": [""Journey Builder"",""CRM"",""API"",""WhatsApp"",""SMS""],
      ""L_c_count"": 2,
      ""T_u_count"": 5,
      ""M_g_count"": 3,
      ""S_e_count"": 1,
      ""O_s_count"": 2
    },
    {
      ""sectionId"": ""S2"",
      ""T_u_list"": [""CRM"",""API"",""missed call""],
      ""L_c_count"": 1,
      ""T_u_count"": 3,
      ""M_g_count"": 1,
      ""S_e_count"": 0,
      ""O_s_count"": 1
    }
  ]
}
";
        string endpoint = "https://api.groq.com/openai/v1/chat/completions";

        using var client = new HttpClient();
        var groqApiKey = Environment.GetEnvironmentVariable("CentauriGroqApiKey");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {groqApiKey}");

        (new FileLogger()).LogWarningAsync($"groq response api key+ {groqApiKey}");

        var requestBody = new
        {
            model = "llama-3.3-70b-versatile",
            messages = new[] {
            new { role = "system", content = bulkSystemPrompt },
            new { role = "user", content = payload }
        },
            response_format = new { type = "json_object" },
            temperature = 0 // Lowest temperature for maximum logic 
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(endpoint, content);
            var jsonResponse = await response.Content.ReadAsStringAsync();
            (new FileLogger()).LogWarningAsync($"groq response + {jsonResponse}");
            using var doc = JsonDocument.Parse(jsonResponse);
            var data = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            var res = JsonSerializer.Deserialize<GroqBulkResponse>(data);
            return res;
        }
        catch (Exception ex)
        {
            (new FileLogger()).LogWarningAsync($"error in groq response + {ex.ToString()}");
            return null;
        }
    }
    public async Task<GroqSectionResult> AnalyzeArticleExpertise(string fullArticle)
    {
        string systemPrompt = @"
### ROLE
You are an Advanced SEO Semantic Analyst.

Your task is to audit the ENTIRE ARTICLE for E-E-A-T markers and return OVERALL counts (not per section).

-----------------------------------------------------------------
### MARKER DEFINITIONS & GUARDRAILS

1. Technical Breadth (T_u)
Definition: Unique domain-specific nouns, acronyms, or jargon defining the niche.
Restriction:
- Must appear ≥2 times across the article OR be explicitly defined.
- Ignore corporate fluff.
- Count each distinct term once.

2. Analytical Depth (L_c)
Definition: Logical bridges connecting premise → technical consequence.
Restriction:
- Exclude simple conjunctions (and, also).
- Must change meaning if removed (Substitution Test).

3. Empirical Grounding (M_g)
Definition: Verifiable anchors such as:
- Metrics
- Legal codes
- Named regulatory bodies

Restriction:
- Categorize internally as [Metric], [Regulatory/Legal], or [Proper Noun/Body].
- Generalities score 0.

4. Practitioner Persona (S_e)
Definition: Language indicating real-world navigation, warnings, methodology, or scar tissue.
Restriction:
- Must include a concrete detail within 1–2 sentences.

5. Operational Specificity (O_s)
Definition: Executable technical actions.

A sentence qualifies if it clearly contains at least 3 of:
[Trigger] + [Action] + [Object] + [Outcome]

-----------------------------------------------------------------
### INTERNAL PROCESS (DO NOT OUTPUT)
1. Extract all candidate markers across the entire article.
2. Apply restrictions strictly.
3. Recalculate counts.
4. Ensure T_u_count equals T_u_list length.
5. Then produce final JSON.

-----------------------------------------------------------------
### OUTPUT FORMAT (RETURN JSON ONLY)

{
  ""T_u_list"": [],
  ""T_u_count"": 0,
  ""L_c_count"": 0,
  ""M_g_count"": 0,
  ""S_e_count"": 0,
  ""O_s_count"": 0
}
";
        fullArticle = RemoveHtmlTags(fullArticle);
        string endpoint = "https://api.groq.com/openai/v1/chat/completions";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        (new FileLogger()).LogErrorAsync($"Authorization + $Bearer {_apiKey}");
        var requestBody = new
        {
            model = "llama-3.3-70b-versatile",
            messages = new[]
            {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = $"ARTICLE START\n{fullArticle}\nARTICLE END" }
        },
            response_format = new { type = "json_object" },
            temperature = 0
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        try
        {
            var response = await client.PostAsync(endpoint, content);
            var jsonResponse = await response.Content.ReadAsStringAsync();
            (new FileLogger()).LogErrorAsync($"JsonResponse : {jsonResponse}");
            using var doc = JsonDocument.Parse(jsonResponse);
            var data = doc.RootElement
                          .GetProperty("choices")[0]
                          .GetProperty("message")
                          .GetProperty("content")
                          .GetString();

            var result = JsonSerializer.Deserialize<GroqSectionResult>(data);

            return result;
        }
        catch(Exception ex)
        {
            (new FileLogger()).LogErrorAsync($"Exception : {ex.ToString()}");
            return null;
        }
    }
    public static string RemoveHtmlTags(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // 1. Remove script and style blocks completely
        string noScript = Regex.Replace(input,
            @"<script[^>]*>.*?</script>",
            "",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        string noStyle = Regex.Replace(noScript,
            @"<style[^>]*>.*?</style>",
            "",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // 2. Remove all remaining HTML tags
        string noHtml = Regex.Replace(noStyle,
            @"<[^>]+>",
            " ");

        // 3. Decode HTML entities (&nbsp;, &amp;, etc.)
        string decoded = WebUtility.HtmlDecode(noHtml);

        // 4. Normalize whitespace
        string normalized = Regex.Replace(decoded,
            @"\s+",
            " ").Trim();

        return normalized;
    }
    public double CalculateArticleExpertiseScore(GroqSectionResult res, List<GeminiSentenceTag> sentences)
    {
        int wordCount = 0;
        double lengthFactor = wordCount > 2500 ? 0.9 : 1.0;
        sentences.ForEach(s =>
        {
           wordCount += s.Sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        });

        int sentenceCount = sentences
            .Count;

        if (wordCount == 0 || sentenceCount == 0)
            return 0;

        double P1 = wordCount == 0? 0 : Math.Min((res.TuCount / (double)wordCount) / 0.02, 1.0);  // 2%
        double P2 = sentenceCount == 0? 0:Math.Min((res.LcCount / (double)sentenceCount) / 0.15, 1.0); // 15%
        double P3 = sentenceCount == 0 ? 0 : Math.Min((res.MgCount / (double)sentenceCount) / 0.15, 1.0); // 15%
        double P4 = sentenceCount == 0 ? 0 : Math.Min((res.SeCount / (double)sentenceCount) / 0.10, 1.0); // 10%
        double P5 = sentenceCount == 0 ? 0 : Math.Min((res.OsCount / (double)sentenceCount) / 0.12, 1.0); // 12%

        double Es = 10 * (0.15 * P1 + 0.15 * P2 + 0.20 * P3 + 0.20 * P4 + 0.30 * P5);
        Es *= lengthFactor;
        return Math.Round(Es, 2);
    }
    public async Task<ExpertiseFinalResult> AnalyzeExpertise(List<Section> mySections)
    {
        // A. Groq API Call (Using your 5-marker Prompt)
        var groqInput = mySections.Select(s => new {
            sectionId = s.Id,
            content = string.Join(" ", s.Sentences)
        }).ToList();

        string userJson = System.Text.Json.JsonSerializer.Serialize(groqInput);
        var groqData = await GetExpertise(userJson);

        double totalWeightedScore = 0;
        double totalWeight = 0;
        var analysisList = new List<SectionAnalysis>();
        int totalSections = groqData.Results.Count;

        for (int i = 0; i < totalSections; i++)
        {
            var res = groqData.Results[i];
            var original = mySections.First(x => x.Id == res.SectionId);

            // Word count aur Sentence count calculation
            int wordCount = string.Join(" ", original.Sentences).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            int sentenceCount = original.Sentences.Count;
            if (wordCount == 0 || sentenceCount == 0) continue;

            // --- CORE ANALYTICS: DENSITY NORMALIZATION ---
            // P1: Technical Density (Goal: 1 technical term every 10 words)
            double P1 = Math.Min((res.TuCount / (double)wordCount) / 0.10, 1.0);

            // P2: Causal Density (Goal: 1 logic bridge every 3 sentences)
            double P2 = Math.Min((res.LcCount / (double)sentenceCount) / 0.33, 1.0);

            // P3: Grounding Density (Goal: 1 data point every 4 sentences)
            double P3 = Math.Min((res.MgCount / (double)sentenceCount) / 0.25, 1.0);

            // P4: Experience Density (Goal: 1 practitioner signal every 5 sentences)
            double P4 = Math.Min((res.SeCount / (double)sentenceCount) / 0.20, 1.0);

            // P5: Operational Specificity (Goal: 1 strict O_s every 5 sentences)
            // Note: O_s is high value, so we benchmark it at 20% of content.
            double P5 = Math.Min((res.OsCount / (double)sentenceCount) / 0.20, 1.0);

            // --- EXPERTISE SCORE (Es) WEIGHTED SUM ---
            // O_s (P5) ko 30% weight diya hai kyunki ye sabse bada Expertise indicator hai.
            double Es = 10 * (0.15 * P1 + 0.15 * P2 + 0.20 * P3 + 0.20 * P4 + 0.30 * P5);

            // --- SIGNIFICANCE FACTOR (Sf) LOGIC ---
            double Sf = 1.0;
            if (i == 0 || i == totalSections - 1)
            {
                Sf = 0.5; // Intro/Outro are less significant for deep expertise
            }
            else
            {
                // Technical deep-dive sections get higher weight
                Sf = 1.3;
                // Bonus: Agar section mein Table hai toh aur significance badhao
                if (original.SectionText.Contains("<table>")) Sf = 1.5;
            }

            double W = wordCount * Sf;
            totalWeightedScore += (Es * W);
            totalWeight += W;

            analysisList.Add(new SectionAnalysis
            {
                SectionId = res.SectionId,
                Header = original.SectionText,
                SectionScore = Math.Round(Es, 2),
                Weight = W,
                // Debugging ke liye individual P-scores bhi save kar sakte ho
            });
        }

        double globalScore = totalWeight > 0 ? totalWeightedScore / totalWeight : 0;

        return new ExpertiseFinalResult
        {
            GlobalArticleScore = Math.Round(globalScore, 2),
            SectionDetails = analysisList
        };
    }
    // Low-level analyze (kept for compatibility)
    // Sends an OpenAI-compatible chat/completions payload and returns assistant content (machine-parsable JSON expected).
    public async Task<List<PerplexitySentenceTag>> AnalyzeAsync(string payload, string systemRequirement)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        var provider = "groq:analyze";

        // Use a short-lived HttpClient with DNS re-resolve as you had before, but reuse request format below.
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2) // Re-resolve DNS periodically
        };
        using var client = new HttpClient(handler) { BaseAddress = _baseUri };

        if (!string.IsNullOrWhiteSpace(_apiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);


        string rawResponse = null;
        var retryCount = 0;
        var isSuccessful = false;
        var exceptionPlaceholder = "";
        string assistantContent = rawResponse;

        while (retryCount < 2 && !isSuccessful)
        {

            try
            {
                var requestBody = new
                {
                    model = "llama-3.1-8b-instant",
                    messages = new[]
        {
                new { role = "system", content = SentenceTaggingPrompts.GroqRevisedPrompt},
                new { role = "user", content = payload + (!string.IsNullOrEmpty(exceptionPlaceholder)?exceptionPlaceholder:"")
    }
            },
                    temperature = 0.0,
                    max_tokens = 32000
                };
                using var stringContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                rawResponse = await _aiCallTracker.TrackAsync(
                requestBody,
                    async () =>
                    {
                        var res = await client.PostAsync("/openai/v1/chat/completions", stringContent);
                        var r = await res.Content.ReadAsStringAsync();
                        return (r, (JsonSerializer.Deserialize<GroqUsageResponse>(r, options)).Usage);

                    },
                    "groq:llama-3.1-8b-instant"
                );

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
                if (!string.IsNullOrWhiteSpace(assistantContent))
                {
                    string json = assistantContent
                        .Replace("```json", "")
                        .Replace("```", "")
                        .Trim();
                    var start = json.IndexOf("[");
                    var end = json.LastIndexOf("]");
                    string inner = "[" + json.Substring(start + 1, end - start - 1) + "]";
                    json = inner;
                    var re = JsonSerializer.Deserialize<List<PerplexitySentenceTag>>(json, options);
                    isSuccessful = true;
                    return re;

                }
            }
            catch (Exception ex)
            {
                exceptionPlaceholder+= ex.Message + " ";
                retryCount++;
                await _logger.LogErrorAsync($"Error occured in AnalyzeAsync : GroqClient : {ex.Message}{ex.StackTrace}");
            }
        }
    
    return null;
    }

    public async Task<IReadOnlyList<PerplexitySentenceTag>> TagArticleAsync(string article)
    {
        const string provider = "Groq:TagArticle";
        _llmLogger.LogDebug("TagArticleAsync started");
        var stopwatch = Stopwatch.StartNew();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        try
        {
            if (string.IsNullOrWhiteSpace(article))
                throw new LlmValidationException("Article cannot be null or empty", provider, new List<string> { "Invalid article" });

            var apiResponse = await _cacheManager.ExecuteWithCacheAsync<List<PerplexitySentenceTag>>(
                provider,
                article,
                () => AnalyzeAsync(article, string.Empty)
            );

            if (apiResponse == null || apiResponse.Count == 0)
            {
                _llmLogger.LogWarning("TagArticleAsync returned empty response");
                stopwatch.Stop();
                _llmLogger.LogApiCall(provider, "Tag Article", stopwatch.ElapsedMilliseconds, true);
                return new List<PerplexitySentenceTag>();
            }

            stopwatch.Stop();
            _llmLogger.LogApiCall(provider, "Tag Article", stopwatch.ElapsedMilliseconds, true);
            return apiResponse;
        }
        catch (LlmOperationException)
        {
            stopwatch.Stop();
            _llmLogger.LogApiCall(provider, "Tag Article", stopwatch.ElapsedMilliseconds, false, "Operation failed");
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _llmLogger.LogError("TagArticleAsync failed", ex, new Dictionary<string, object>
            {
                { "DurationMs", stopwatch.ElapsedMilliseconds },
                { "Provider", provider }
            });
            throw new LlmApiException("Failed to tag article with Groq", provider, null, ex.Message, ex);
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

public class GroqHeadingsResponse
{
    public List<string> categories { get; set; }
}

public class GroqBulkResponse
{
    [JsonPropertyName("results")]
    public List<GroqSectionResult> Results { get; set; }
}

public class GroqSectionResult
{
    [JsonPropertyName("sectionId")]
    public string SectionId { get; set; }

    [JsonPropertyName("T_u_count")]
    public int TuCount { get; set; }

    [JsonPropertyName("L_c_count")]
    public int LcCount { get; set; }

    [JsonPropertyName("M_g_count")]
    public int MgCount { get; set; }

    [JsonPropertyName("S_e_count")]
    public int SeCount { get; set; }
    [JsonPropertyName("O_s_count")]
    public int OsCount { get; set; }
    

    [JsonPropertyName("T_u_list")]
    public List<string> Entities { get; set; }
}

// 3. Final Result Store karne ke liye
public class ExpertiseFinalResult
{
    public double GlobalArticleScore { get; set; }
    public List<SectionAnalysis> SectionDetails { get; set; }
}

public class SectionAnalysis
{
    public string SectionId { get; set; }
    public string Header { get; set; }
    public double SectionScore { get; set; } // Es
    public double Weight { get; set; } // W
}
public class GroqUsageDetails
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }

    [JsonPropertyName("compute_time_ms")]
    public int ComputeTimeMs { get; set; }
}

public class GroqUsageResponse
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; }

    [JsonPropertyName("usage")]
    public GroqUsageDetails Usage { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}
