using CentauriSeo.Core.Models.Outputs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Scoring
{
    public class SignalToNoiseScorer
    {
        public static double Score(OrchestratorResponse orchestratorResponse)
        {
            var S = orchestratorResponse.ValidatedSentences.Where(x => x.InformativeType == Enums.InformativeType.Fact ||
            x.InformativeType == Enums.InformativeType.Claim || x.InformativeType == Enums.InformativeType.Definition ||
            x.InformativeType == Enums.InformativeType.Statistic).Count();

            var N = orchestratorResponse.ValidatedSentences.Where(x => x.InformativeType == Enums.InformativeType.Filler ||
            x.InformativeType == Enums.InformativeType.Transition || x.InformativeType == Enums.InformativeType.Uncertain ||
            x.Structure == Enums.SentenceStructure.Fragment).Count();
            if(S+N == 0)
            {
                return 0;
            }

            var SN_base = (double)S / (S + N);

            var sentencesWithSameParagraphAndHavingconsecutiveNoise = orchestratorResponse.ValidatedSentences
                .Where(x => x.InformativeType == Enums.InformativeType.Filler ||
                x.InformativeType == Enums.InformativeType.Transition ||
                x.InformativeType == Enums.InformativeType.Uncertain ||
                x.Structure == Enums.SentenceStructure.Fragment)
                .GroupBy(x => x.ParagraphId)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g)
                .ToList();

            var c = sentencesWithSameParagraphAndHavingconsecutiveNoise?.DistinctBy(x => x.ParagraphId).Count();

            if(c != null)
            {
                var maxPenalty = 0.15;
                var penalty = 0.05;
                while (c > 0 && penalty <=maxPenalty)
                {

                    
                    SN_base -= penalty;
                    penalty += 0.05;
                    c--;
                }
            }

            return SN_base;
        }
    }
}
