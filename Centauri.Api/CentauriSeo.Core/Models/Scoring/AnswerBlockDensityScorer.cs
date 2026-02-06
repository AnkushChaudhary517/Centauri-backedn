using CentauriSeo.Core.Models.Enums;
using CentauriSeo.Core.Models.Outputs;
using CentauriSeo.Core.Models.Sentences;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Scoring
{
    public class AnswerBlockDensityScorer
    {
        public static double Score(OrchestratorResponse orchestratorResponse)
        {
            if(orchestratorResponse.ValidatedSentences.All(x => x.AnswerSentenceFlag == 1))
            {
                return 0.0;
            }else
            {
                return orchestratorResponse.AnswerPositionIndex.PositionScore;
            }
        }
    }
}
