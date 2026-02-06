using CentauriSeo.Core.Models.Outputs;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Scoring
{
    public class TechnicalClarityScorer
    {
        public static double Score(OrchestratorResponse orchestratorResponse)
        {

            var structureScore = 0.0;
            var G = 0;
            orchestratorResponse.ValidatedSentences?.ToList()?.ForEach(s =>
            {
                switch(s.Structure)
                {
                    case Enums.SentenceStructure.Simple:
                        structureScore += 1.0;
                        break;
                        case Enums.SentenceStructure.Compound:
                    case Enums.SentenceStructure.Complex:
                        structureScore += 0.75;
                        break;  
                    case Enums.SentenceStructure.CompoundComplex:
                        structureScore += 0.5;
                        break;
                }

                if(s.IsGrammaticallyCorrect)
                {
                    G += 1;
                }
            });
            var CS = ClaritySynthesisScorer.Score(orchestratorResponse.ValidatedSentences);
            var SQ = (double)structureScore / (orchestratorResponse.ValidatedSentences?.Count() ?? 1);
            var GA = 0.0;
            var T = orchestratorResponse.ValidatedSentences.Count();
            if (orchestratorResponse?.ValidatedSentences == null || orchestratorResponse.ValidatedSentences.Count() == 0)
            {
                GA = 0;
            }
            else {
                GA = (double)G / T;
            }
            return  (0.4 * GA) + (0.35 * SQ) + (0.25 * CS)
;
        }
    }
}
