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
            var sentences = orchestratorResponse.ValidatedSentences;
            if (sentences == null || !sentences.Any()) return 0;

            // 1. Signal (Value-adding content)
            var S = sentences.Count(x => x.InformativeType == Enums.InformativeType.Fact ||
                                         x.InformativeType == Enums.InformativeType.Claim ||
                                         x.InformativeType == Enums.InformativeType.Definition ||
                                         x.InformativeType == Enums.InformativeType.Statistic);

            // 2. Noise (Fillers and fluff)
            var N = sentences.Count(x => x.InformativeType == Enums.InformativeType.Filler ||
                                         x.InformativeType == Enums.InformativeType.Transition ||
                                         x.InformativeType == Enums.InformativeType.Uncertain);

            if (S + N == 0) return 0;

            // snBase is between 0.0 and 1.0
            double snBase = (double)S / (S + N);

            // 3. Cluster Penalty
            var noisyParagraphsCount = sentences
                .GroupBy(x => x.ParagraphId)
                .Count(g => g.Count(s => s.InformativeType == Enums.InformativeType.Filler) > 1);

            // Penalty logic (Max 0.20 reduction from the base ratio)
            double totalPenalty = Math.Min(0.20, noisyParagraphsCount * 0.02);

            // Result calculation on a 0.0 to 1.0 scale
            double finalRatio = Math.Max(0, snBase - totalPenalty);

            // 4. Adjusting to Base 10
            // Direct multiplication by 10 to scale from 0..1 to 0..20
            return Math.Round(finalRatio * 10, 2);
        }
    }
}
