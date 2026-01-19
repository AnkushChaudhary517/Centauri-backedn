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
        public AnswerPositionIndex AnswerPositionIndex
        {
            get
            {
                if (Sentences == null || Sentences.Count == 0)
                {
                    return new AnswerPositionIndex { PositionScore = 0.0 };
                }

                for (int i = 0; i < Sentences.Count; i++)
                {
                    if (Sentences[i].AnswerSentenceFlag?.Value == 1)
                    {
                        return new AnswerPositionIndex
                        {
                            // 1-based position normalized by total sentence count
                            PositionScore = ((double)(i + 1)) / Sentences.Count,
                            FirstAnswerSentenceId = Sentences[i].Id
                        };
                    }
                }

                // No answer sentence found
                return new AnswerPositionIndex { PositionScore = 0.0 };
            }
        }

    }
}
