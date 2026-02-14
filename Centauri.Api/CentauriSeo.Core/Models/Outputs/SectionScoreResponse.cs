using CentauriSeo.Core.Models.Output;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Outputs
{
    public class SectionScoreResponse
    {
        public Intent Intent { get; set; }
        public string KeyWord { get; set; } = "";
        public List<CompetitorSectionScoreResponse> Competitors { get; set; } = new List<CompetitorSectionScoreResponse>();
        public List<Variant2> Variants { get; set; } = new List<Variant2>();

    }

    public class Variant2
    {
        public string Text { get; set; }
        public Variant2Type VariantType { get; set; }
    }
    public class CompetitorSectionScoreResponse
    {
        public string Url { get; set; } = "";
        public List<string> Headings { get; set; } = new List<string>();
        public Intent Intent { get; set; }        
    }

    public enum Intent
    {
        Informational,
        Navigational,
        Transactional,
        Commercial
    }
    public enum Variant2Type
    {
        Exact,
        Lexical,
        Semantic,
        Morphological,
        SearchDerived
    }
}
