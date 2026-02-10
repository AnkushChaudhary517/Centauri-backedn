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
            if (orchestratorResponse?.ValidatedSentences == null || !orchestratorResponse.ValidatedSentences.Any())
                return 0;

            // 1. Saare facts nikal lo
            var factualList = orchestratorResponse.ValidatedSentences
                .Where(x => x.InformativeType == Enums.InformativeType.Fact ||
                            x.InformativeType == Enums.InformativeType.Statistic ||
                            x.InformativeType == Enums.InformativeType.Definition)
                .ToList();

            // Agar article mein koi fact hi nahi hai, toh isolation score 0
            if (factualList.Count == 0) return 0;

            // 2. Wo Paragraphs dhoondo jo "Purely Informative" hain (Authority Blocks)
            var pragraphIdsWithOnlyFacts = orchestratorResponse.ValidatedSentences
                .GroupBy(x => x.ParagraphId)
                .Where(g => g.All(s => s.InformativeType == Enums.InformativeType.Fact ||
                                       s.InformativeType == Enums.InformativeType.Statistic ||
                                       s.InformativeType == Enums.InformativeType.Definition))
                .Select(g => g.Key)
                .ToHashSet();

            // 3. Isolated facts count karo
            var isolatedFactualSentencesCount = factualList.Count(x => pragraphIdsWithOnlyFacts.Contains(x.ParagraphId));

            // 4. Ratio: (Isolated Facts / Total Facts)
            double isolationRatio = (double)isolatedFactualSentencesCount / factualList.Count;

            // 5. Adjusting to Base 10
            // Result will be between 0.00 and 10.00
            return Math.Round(isolationRatio * 10, 2);
        }
    }
}
