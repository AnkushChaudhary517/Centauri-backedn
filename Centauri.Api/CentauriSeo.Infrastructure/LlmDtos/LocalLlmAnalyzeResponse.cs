using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Infrastructure.LlmDtos
{
    public class LocalLlmAnalyzeResponse
    {
        public List<GeminiSentenceTag> sentences { get; set; } = new List<GeminiSentenceTag>();
        public string answerPositionIndex { get; set; }
    }
}
