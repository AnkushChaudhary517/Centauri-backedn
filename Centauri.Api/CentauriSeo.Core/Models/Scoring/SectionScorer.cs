using CentauriSeo.Core.Models.Outputs;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CentauriSeo.Application.Scoring;

public static class SectionScorer
{
    // Returns 0..10 (approximation when SERP-derived required subtopics are not available)
    //public static double Score(IEnumerable<ValidatedSentence> sentences, string? primaryKeyword)
    //{
    //    // Proxy: count distinct informative types as a lightweight subtopic coverage proxy.
    //    // RS_total (expected required subtopics) approximated as 6.
    //    const double RS_total = 6.0;
    //    var typesCovered = sentences.Select(s => s.InformativeType).Distinct().Count();
    //    double RS_covered = Math.Min(typesCovered, RS_total);

    //    // No original subtopic detection (OG = 0)
    //    double OG = 0.0;

    //    double sectionScore = (RS_covered / RS_total) * 10.0 * (1.0 + OG / RS_total);
    //    return Math.Clamp(sectionScore, 0.0, 10.0);
    //}

    public static double Calculate(
            List<CompetitorSectionScoreResponse> competitors,
            List<string> yourHeadings)
    {
        // Normalize competitor headings
        var competitorMap = BuildSubtopicFrequencyMap(competitors);
        var requiredSubtopics = GetRequiredSubtopicsFromLocalLlm(competitors);

        // Required Subtopics = appear in >= 3 competitors
        //var requiredSubtopics = competitorMap
        //    .Where(x => x.Value.Count >= 3)
        //    .Select(x => x.Key)
        //    .ToList();

        //if(requiredSubtopics == null || requiredSubtopics.Count==0)
        //{
        //    requiredSubtopics = competitorMap
        //                    .OrderByDescending(x => x.Value.Count)
        //    //.Take(5)
        //    .Select(x => x.Key)
        //    .ToList();
        //}

        int RS_total = requiredSubtopics.Count;

        // Normalize your headings
        var normalizedYourHeadings = yourHeadings
            .Select(Normalize)
            .ToHashSet();

        List<SentenceSimilarityInput>inputs = new List<SentenceSimilarityInput>();
        requiredSubtopics?.ForEach(requiredSubtopic =>
        {
            normalizedYourHeadings?.ToList()?.ForEach(heading =>
            {
                inputs.Add(new SentenceSimilarityInput()
                {
                    Text1 = heading,
                    Text2 = requiredSubtopic,
                });
            });
        });

        var similarities = GetFullArticleSimilarities(inputs);

        // RS_covered
        var covered = requiredSubtopics
    .Where(rs =>
        normalizedYourHeadings.Any(h =>
        {
            var key = h + rs;
            var similarity = similarities.FirstOrDefault(x => x.Key == key);
            return similarity != null && similarity.Similarity >= 0.5;
        })
    )
    .ToList();

        int RS_covered = covered.Count;

        // Missing required
        var missingRequired = requiredSubtopics
            .Except(covered)
            .ToList();

        // OG = your headings not in competitors
        var originalSubtopics = normalizedYourHeadings
            .Where(h => !competitorMap.ContainsKey(h))
            .ToList();

        int OG = originalSubtopics.Count;

        // Score formula
        double score = RS_total == 0
            ? 0
            : ((double)RS_covered / RS_total) * (1 + ((double)OG / RS_total));//need to check if we have to multiply with 10 or not

        //return new SectionScoreResult
        //{
        //    RS_Total = RS_total,
        //    RS_Covered = RS_covered,
        //    OG = OG,
        //    SectionScore = Math.Round(score, 2),
        //    MissingRequiredSubtopics = missingRequired,
        //    OriginalSubtopics = originalSubtopics
        //};
        return Math.Round(score, 2);
    }

    // ----------- HELPERS -----------
    public static double Similarity(string s1, string s2) /// is a form  is the form misising ashjh
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
            return 0;

        int distance = LevenshteinDistance(s1, s2);
        int maxLen = Math.Max(s1.Length, s2.Length);

        return 1.0 - (double)distance / maxLen;
    }

    public static List<string> GetRequiredSubtopicsFromLocalLlm(List<CompetitorSectionScoreResponse> data)
    {
        HttpClient client = new HttpClient();
        string apiUrl = "http://ec2-15-206-164-71.ap-south-1.compute.amazonaws.com:8000/get-subtopics";
        //string apiUrl = "http://localhost:8000/get-subtopics";
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };
            var inputData = JsonSerializer.Serialize(new
            {
                data = data.ConvertAll(x => new
                {
                    Headings = x.Headings,
                    Url = x.Url,
                    Intent = (int)x.Intent
                })
            }, options);
            var content = new StringContent(inputData, System.Text.Encoding.UTF8, "application/json");
            // 3. Make the POST request
            var response = client.PostAsync(apiUrl, content).Result;

            if (response.IsSuccessStatusCode)
            {
                // 4. Deserialize the response
                var res = response.Content.ReadAsStringAsync().Result;
                return JsonSerializer.Deserialize<List<string>>(res, options);

            }
            else
            {
                return null;
            }
        }
        catch (Exception ex)
        {
        }
        return null;
    }

    public static List<SentenceSimilarityOutput> GetFullArticleSimilarities(List<SentenceSimilarityInput> input)
    {
        HttpClient client = new HttpClient();
        string apiUrl = "http://ec2-15-206-164-71.ap-south-1.compute.amazonaws.com:8000/similarity/batch";
        //string apiUrl = "http://localhost:8000/similarity/batch";
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };
            var inputData = JsonSerializer.Serialize(new
            {
                items=input?.Select(x => new
                {
                    text1=x.Text1,
                    text2=x.Text2
                })
            }, options);
            var content = new StringContent(inputData, System.Text.Encoding.UTF8, "application/json");
            // 3. Make the POST request
            var response = client.PostAsync(apiUrl, content).Result;

            if (response.IsSuccessStatusCode)
            {
                // 4. Deserialize the response
                var res = response.Content.ReadAsStringAsync().Result;
                var results = JsonSerializer.Deserialize<SentenceSimilarityOutputLlmResponse>(res, options);
                if(results?.Similarities != null && results?.Similarities.Count > 0)
                {
                    List<SentenceSimilarityOutput> outputs = new List<SentenceSimilarityOutput>();
                    for (int i = 0; i < results?.Similarities.Count; i++) {
                        outputs.Add( new SentenceSimilarityOutput()
                        {
                            Key = input[i].Key,
                            Similarity = results.Similarities[i],
                        });
                    }
                    return outputs;
                }

            }
            else
            {
            }
        }
        catch (Exception ex)
        {
        }
        return null;
    }

    public static SentenceSimilarityOutput GetSimilarity(string text1, string text2)
    {
        HttpClient client = new HttpClient();
        string apiUrl = "http://ec2-15-206-164-71.ap-south-1.compute.amazonaws.com:8000/similarity";
        //string apiUrl = "http://localhost:8000/similarity";
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };
            var inputData = JsonSerializer.Serialize(new 
            {
                text1 = text1,
                text2 = text2
            },options);
            var content = new StringContent(inputData, System.Text.Encoding.UTF8, "application/json");
            // 3. Make the POST request
            var response = client.PostAsync(apiUrl, content).Result;

            if (response.IsSuccessStatusCode)
            {
                // 4. Deserialize the response
                var res = response.Content.ReadAsStringAsync().Result;
                var results = JsonSerializer.Deserialize<SentenceSimilarityOutput>(res, options);
                return results;
            }
            else
            {
            }
        }
        catch (Exception ex)
        {
        }
        return null;
    }

    private static int LevenshteinDistance(string s, string t)
    {
        var dp = new int[s.Length + 1, t.Length + 1];

        for (int i = 0; i <= s.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= t.Length; j++) dp[0, j] = j;

        for (int i = 1; i <= s.Length; i++)
        {
            for (int j = 1; j <= t.Length; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;

                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost
                );
            }
        }

        return dp[s.Length, t.Length];
    }

    private static Dictionary<string, HashSet<string>> BuildSubtopicFrequencyMap(
        List<CompetitorSectionScoreResponse> competitors)
    {
        var map = new Dictionary<string, HashSet<string>>();

        foreach (var competitor in competitors)
        {
            foreach (var heading in competitor.Headings)
            {
                var key = Normalize(heading);

                if (!map.ContainsKey(key))
                    map[key] = new HashSet<string>();

                map[key].Add(competitor.Url);
            }
        }

        return map;
    }

    private static string Normalize(string text)
    {
        text = text.ToLowerInvariant();
        text = Regex.Replace(text, @"\d+", "");
        text = Regex.Replace(text, @"[^\w\s]", "");
        text = Regex.Replace(text,
            @"\b(how|what|why|guide|overview|explained)\b", "");
        text = Regex.Replace(text, @"\s+", " ");

        // Synonym normalization
        text = text.Replace("pricing", "cost");
        text = text.Replace("price", "cost");
        text = text.Replace("fees", "cost");
        text = Regex.Replace(
        text,
        @"^\s*(
            (\(?\d+[\.\)])|        # 1. 1) (1)
            (\(?[ivxlcdm]+[\.\)])| # i. ii) iv.
            (\(?[a-z][\.\)])|      # a. b)
            [-•–—]                 # bullets
        )\s*",
        "",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace
    );

        text = Regex.Replace(text, @"^\s*[ivxlcdm]+\s+", "", RegexOptions.IgnoreCase);

        return text.Trim();
    }
}
