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
        public AnswerPositionIndex AnswerPositionIndex { get; set; }
        public List<Section> Sections { get; set; } = new List<Section>();
    }

    public class AnswerSentenceFlag {
        public int Value { get; set; }
        public string  Reason { get; set; }
    }
    public class EntityMentionFlag {
        public int Value { get; set; }
        public int EntityCount { get; set; }
        public List<string> Entities { get; set; } = new List<string>();
    }
    public class EntityConfidenceFlag {
        public int Value { get; set; }
    }
    public class AnswerPositionIndex {
        public string? FirstAnswerSentenceId { get; set; }
        public double PositionScore { get; set; } // we will calculate this based on the position of the first answer sentence
    }

}
