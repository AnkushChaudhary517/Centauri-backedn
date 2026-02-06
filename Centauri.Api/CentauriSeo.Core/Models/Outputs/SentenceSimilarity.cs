using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Outputs
{
    public class SentenceSimilarityInput
    {
        public string Text1 { get; set; }
        public string Text2 { get; set; }

        public string Key { get { 
            return Text1 + Text2;
            } }
    }
    public class SentenceSimilarityOutput
    {
        public string Key { get; set; }
        public double Similarity { get; set; }
    }
    public class SentenceSimilarityOutputLlmResponse
    {
        public string Key { get; set; }
        public List<double> Similarities { get; set; }
    }
}
