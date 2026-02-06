using CentauriSeo.Core.Models.Outputs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Scoring
{
    public class EntityAlignmentScorer
    {
        public static double Score(OrchestratorResponse orchestratorResponse)
        {
            var E = orchestratorResponse.ValidatedSentences.Where(x=>x.EntityMentionFlag != null).Sum(vs =>  vs.EntityMentionFlag.Value);
            if(E == 0)
            {
                return 0;
            }
            var C = orchestratorResponse.ValidatedSentences.Where(x => x.EntityConfidenceFlag == 1).Count();
            return (double)C/ E;
        }
    }
}
