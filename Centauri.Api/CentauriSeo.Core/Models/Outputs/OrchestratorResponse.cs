using CentauriSeo.Core.Models.Sentences;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Outputs
{
    public class OrchestratorResponse
    {
        public IReadOnlyList<ValidatedSentence> ValidatedSentences { get; set; } = Array.Empty<ValidatedSentence>();
        public double PlagiarismScore { get; set; } = 0.0;
        public double SectionScore { get; set; }
        public double IntentScore { get; set; }
        public double KeywordScore { get; set; }
    }
}
