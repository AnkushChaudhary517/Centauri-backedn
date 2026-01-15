using CentauriSeo.Core.Models.Sentences;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Scoring
{
    public class ClaritySynthesisScorer
    {
        public static double Score(IEnumerable<ValidatedSentence> sentences)
        {
            var list = sentences.ToList();
            if (!list.Any()) return 0.0;

            double totalCount = 0.0;

            foreach (var s in list)
            {
                if (s.ClaritySynthesisType == Enums.ClaritySynthesisType.Focused)
                    totalCount += 1.0;
                else if (s.ClaritySynthesisType == Enums.ClaritySynthesisType.ModerateComplexity)
                    totalCount += 0.5;
                else if (s.ClaritySynthesisType == Enums.ClaritySynthesisType.LowClarity)
                    totalCount += 0.1;
                else if (s.ClaritySynthesisType == Enums.ClaritySynthesisType.UnIndexable)
                    totalCount += 0.0;

            }

            double A_percent = totalCount / list.Count * 100.0;
            return Math.Clamp(A_percent / 10.0, 0.0, 10.0);
        }
    }
}
