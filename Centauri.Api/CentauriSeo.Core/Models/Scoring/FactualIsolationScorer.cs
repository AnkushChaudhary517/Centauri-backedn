using CentauriSeo.Core.Models.Outputs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Scoring
{
    public class FactualIsolationScorer
    {
        public static double Score(OrchestratorResponse orchestratorResponse)
        {
            var factualList = orchestratorResponse.ValidatedSentences.Where(x => x.InformativeType == Enums.InformativeType.Fact || x.InformativeType == Enums.InformativeType.Statistic);
             var pragraphIdsWithOnlyFacts = orchestratorResponse.ValidatedSentences
                .GroupBy(x => x.ParagraphId)
                .Where(g => g.All(s => s.InformativeType == Enums.InformativeType.Fact || s.InformativeType == Enums.InformativeType.Statistic || s.InformativeType == Enums.InformativeType.Definition))
                .Select(g => g.Key)
                .ToHashSet();

            var isolatedFactualSentences = factualList.Where(x => pragraphIdsWithOnlyFacts.Contains(x.ParagraphId)).ToList();

            return ((double)isolatedFactualSentences.Count) / (double)orchestratorResponse.ValidatedSentences.Count;
        }
    }
}
