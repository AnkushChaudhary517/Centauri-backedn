using CentauriSeo.Core.Models.Enums;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace CentauriSeo.Application.Scoring;

public static class VariationScorer
{
    public static double Score(IEnumerable<ValidatedSentence> sentences)
    {
        if (sentences == null || !sentences.Any())
            return 0;

        int paragraphs = 0;
        int headers = 0;
        int lists = 0;
        int tables = 0;
        int pictures = 0;

        foreach (var sentence in sentences)
        {
            if (string.IsNullOrWhiteSpace(sentence.HtmlTag))
                continue;

            switch (sentence.HtmlTag.ToLower())
            {
                case "p":
                    paragraphs++;
                    break;

                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    headers++;
                    break;

                case "ul":
                case "ol":
                case "li":
                    lists++;
                    break;

                case "table":
                    tables++;
                    break;

                case "img":
                    pictures++;
                    break;
            }
        }

        int total =
            paragraphs +
            headers +
            lists +
            tables +
            pictures;

        if (total == 0)
            return 0;

        // STEP 1 — proportions
        double[] proportions =
        {
            (double)paragraphs / total,
            (double)headers / total,
            (double)lists / total,
            (double)tables / total,
            (double)pictures / total
        };

        // STEP 2 — Shannon Entropy
        double entropy = 0;
        foreach (var p in proportions)
        {
            if (p > 0)
            {
                entropy += -p * Math.Log(p, 2);
            }
        }

        // STEP 3 — Normalize to 0–10
        const double Hmax = 2.321928094887362; // log2(5)
        double varietyScore10 = (entropy / Hmax) * 10;

        return Math.Clamp(varietyScore10, 0.0, 10.0);
    }
}
