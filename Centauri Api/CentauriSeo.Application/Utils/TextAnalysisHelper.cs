using System.Collections.Generic;
using CentauriSeo.Core.Models.Scoring;
using CentauriSeo.Core.Models.Output;

namespace CentauriSeo.Application.Utils;

public static class TextAnalysisHelper
{
    // Returns structured recommendations (Issue, WhatToChange, Examples, Improves)
    public static List<Recommendation> GenerateRecommendations(Level2Scores l2, Level3Scores l3, Level4Scores l4)
    {
        var recommendations = new List<Recommendation>();

        if (l2.SimplicityScore < 2.0)
        {
            recommendations.Add(new Recommendation
            {
                Issue = "Poor sentence simplicity",
                WhatToChange = "Simplify complex and compound-complex sentences. Split long sentences into two short declarative sentences.",
                Examples = new ExamplePair
                {
                    Bad = "Although the setup is complex, the results are worth the effort, and most users complete it within an hour.",
                    Good = "The setup is complex. The results are worth the effort. Most users complete it within an hour."
                },
                Improves = new List<string> { "readability_score", "simplicity_score" }
            });
        }

        if (l2.GrammarScore < 2.0)
        {
            recommendations.Add(new Recommendation
            {
                Issue = "Grammar issues",
                WhatToChange = "Fix grammar errors: ensure sentences start with a capital letter, end with proper punctuation, and correct tense/subject agreement.",
                Examples = new ExamplePair
                {
                    Bad = "the system generate the report successful",
                    Good = "The system generated the report successfully."
                },
                Improves = new List<string> { "grammar_score", "readability_score" }
            });
        }

        if (l2.KeywordScore < 5.0)
        {
            recommendations.Add(new Recommendation
            {
                Issue = "Weak keyword signals",
                WhatToChange = "Add the primary keyword (or a close variant) naturally in high-impact locations: H1, one H2, meta title or meta description. Avoid stuffing.",
                Examples = new ExamplePair
                {
                    Bad = "H1: Improve writing",
                    Good = "H1: How an AI content checker improves on-page SEO"
                },
                Improves = new List<string> { "keyword_score", "relevance_score" }
            });
        }

        if (l3.ReadabilityScore < 7.0)
        {
            recommendations.Add(new Recommendation
            {
                Issue = "Low readability",
                WhatToChange = "Shorten paragraphs, add headings and lists, remove filler sentences and break long paragraphs into focused sections.",
                Examples = new ExamplePair
                {
                    Bad = "A long paragraph containing multiple ideas and no headings.",
                    Good = "Use an H2 to introduce the idea, then 2–3 short paragraphs or a list to explain it."
                },
                Improves = new List<string> { "readability_score" }
            });
        }

        if (l3.EeatScore < 18.0)
        {
            recommendations.Add(new Recommendation
            {
                Issue = "Low EEAT",
                WhatToChange = "Add authoritative citations for statistics, include first-hand observations or internal data, and state author expertise or credentials.",
                Examples = new ExamplePair
                {
                    Bad = "Retention increased by 22 percent.",
                    Good = "Retention increased by 22 percent according to the 2024 SaaS Benchmarks Report."
                },
                Improves = new List<string> { "eeat_score", "credibility_score" }
            });
        }

        if (l4.AiIndexingScore < 50.0)
        {
            recommendations.Add(new Recommendation
            {
                Issue = "Poor AI-indexing signals",
                WhatToChange = "Add clear answer blocks (short direct answers), isolate factual statements from opinion, and use structured headers for subtopics.",
                Examples = new ExamplePair
                {
                    Bad = "An opinion paragraph mixing facts and recommendations.",
                    Good = "Answer: 'Use feature X to achieve Y.' Then a short factual bullet list with sources."
                },
                Improves = new List<string> { "ai_indexing_score", "retrieval_factuality" }
            });
        }

        if (l4.CentauriSeoScore < 40.0)
        {
            recommendations.Add(new Recommendation
            {
                Issue = "Low overall SEO score",
                WhatToChange = "Perform a content revision focusing on missing SERP-derived subtopics, add citations for statistics, and improve readability and keyword placement.",
                Examples = new ExamplePair
                {
                    Bad = "Content lacks sections on key subtopics and contains unsourced stats.",
                    Good = "Add H2s for each required subtopic, include sourced statistics, and improve H1/meta title to include the primary keyword."
                },
                Improves = new List<string> { "centauri_seo_score", "relevance_score", "eeat_score", "readability_score" }
            });
        }

        return recommendations;
    }
}
