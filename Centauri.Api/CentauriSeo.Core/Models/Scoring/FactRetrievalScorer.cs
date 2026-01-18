using CentauriSeo.Core.Models.Sentences;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Scoring
{
    //public class FactRetrievalScorer
    //{
    //    public static double Score(IEnumerable<ValidatedSentence> sentences)
    //    {
    //        var list = sentences.ToList();
    //        if (!list.Any()) return 0.0;

    //        double totalCount = 0.0;

    //        foreach (var s in list)
    //        {
    //            if (s.FactRetrievalType == Enums.FactRetrievalType.VerifiableIsolated)
    //                totalCount += 1.0;
    //            else if (s.FactRetrievalType == Enums.FactRetrievalType.ContexualMixed)
    //                totalCount += 0.5;
    //            else if (s.FactRetrievalType == Enums.FactRetrievalType.Unverifiable)
    //                totalCount += 0.1;
    //            else
    //                totalCount += 0.0;

    //        }

    //        double A_percent = totalCount / list.Count * 100.0;
    //        return Math.Clamp(A_percent / 10.0, 0.0, 10.0);
    //    }
    //}
}
