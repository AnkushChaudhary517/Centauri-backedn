using CentauriSeo.Core.Models.Input;
using CentauriSeo.Core.Models.Sentences;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Application.Pipeline;

public static class Phase0_InputParser
{
    public static List<Sentence> Parse(ArticleInput article)
    {
        var paragraphs = article.Raw.Split("\n\n");
        var sentences = new List<Sentence>();
        int id = 1;

        for (int p = 0; p < paragraphs.Length; p++)
        {
            var parts = paragraphs[p]
                .Split(new[] { ".", "?", "!" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                sentences.Add(new Sentence($"S{id++}", part.Trim(), p));
            }
        }

        return sentences;
    }
}
