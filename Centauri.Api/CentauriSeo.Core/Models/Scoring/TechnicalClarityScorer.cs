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
            if (orchestratorResponse?.ValidatedSentences == null || !orchestratorResponse.ValidatedSentences.Any())
                return 0;

            var sentences = orchestratorResponse.ValidatedSentences;
            var total = (double)sentences.Count();
            var structureScore = 0.0;
            var G = 0;
            var entityBonus = 0.0;

            foreach (var s in sentences)
            {
                // 1. Structure Score (Weighted for Technical Clarity)
                switch (s.Structure)
                {
                    case Enums.SentenceStructure.Simple: structureScore += 1.0; break;
                    case Enums.SentenceStructure.Compound:
                    case Enums.SentenceStructure.Complex: structureScore += 0.85; break;
                    case Enums.SentenceStructure.CompoundComplex: structureScore += 0.5; break;
                    default: structureScore += 0.3; break;
                }

                // 2. Grammar check
                if (s.IsGrammaticallyCorrect || s.Structure == Enums.SentenceStructure.Fragment) G++;

                // 3. Technical Quality (Entity + Relevance)
                // Yahan entityBonus ratio 1.0 se upar nahi jana chahiye per sentence
                if (s.EntityConfidenceFlag > 0 && s.RelevanceScore > 0.5) entityBonus += 1.0;
            }

            // Ratios (All between 0.0 and 1.0)
            var GA = (double)G / total;             // Grammar Accuracy
            var SQ = (double)structureScore / total; // Structure Quality
            var CS = ClaritySynthesisScorer.Score(sentences); // Synthesis (Ensure this returns 0-1)
            var TQ = (double)entityBonus / total;    // Technical Quality factor

            // Final Weighted Score Adjusted to Base 10
            // Sum of weights (0.3 + 0.3 + 0.2 + 0.2) = 1.0
            double weightedSum = (0.3 * GA) + (0.3 * SQ) + (0.2 * CS) + (0.2 * TQ);

            // Scaling to 10
            return Math.Round(weightedSum * 10, 2);
        }
    }
}
