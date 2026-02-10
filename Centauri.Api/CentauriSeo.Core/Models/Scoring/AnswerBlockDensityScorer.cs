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

            var baseDensity = 0.0;
            
            if(orchestratorResponse.ValidatedSentences.All(x => x.AnswerSentenceFlag == 1))
            {
                return 0.0;
            }else
            {
                var index = Int32.Parse(orchestratorResponse.AnswerPositionIndex.FirstAnswerSentenceId.TrimStart('S'))-1;
                return CalculateDensity(index,orchestratorResponse.AnswerPositionIndex.PositionScore);
            }
        }
        public static double CalculateDensity(int firstAnswerIndex, double positionScore)
        {
            double baseDensity;

            // 1. Logic for Base 10 Scaling:
            // Best case (Top 5) = 10.0
            // Mid case (6-15) = 6.0
            // Far case (>15) = 3.0
            // No answer found = 0.0

            if (firstAnswerIndex == -1) // Handling case where no answer is found
            {
                return 0.0;
            }

            // 2. Base Density Logic 
            if (firstAnswerIndex <= 4) // 0, 1, 2, 3, 4 indices (Top 5)
            {
                // 3.33 * 3 roughly gives 10
                baseDensity = 10.0;
            }
            else if (firstAnswerIndex <= 14) // 6th to 15th sentence
            {
                baseDensity = 6.0;
            }
            else
            {
                baseDensity = 3.0;
            }

            // 3. Optional: Agar positionScore (Level 1) ka impact bhi dena hai 
            // toh yahan multiply kar sakte ho, varna baseDensity return karo.
            return baseDensity;
        }
    }
}
