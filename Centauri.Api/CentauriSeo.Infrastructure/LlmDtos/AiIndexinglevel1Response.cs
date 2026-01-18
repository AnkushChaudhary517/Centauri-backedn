using CentauriSeo.Core.Models.Outputs;
using CentauriSeo.Core.Models.Sentences;
using CentauriSeo.Core.Models.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Infrastructure.LlmDtos
{
    public class AiIndexinglevel1Response
    {
    public List<ValidatedSentence> Sentences { get; set; } = new List<ValidatedSentence>();
    public AnswerPositionIndex AnswerPositionIndex { get; set; } = new AnswerPositionIndex();
}
}
