using CentauriSeo.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Sentences;

public class ValidatedSentence
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public string Grammar { get; set; } = "";
    public string HtmlTag { get; set; } = "";

    public InformativeType InformativeType { get; init; }
    public SentenceStructure Structure { get; init; }
    public VoiceType Voice { get; init; }

    public InfoQuality InfoQuality { get; init; }
    public FunctionalType FunctionalType { get; set; }
    public FactRetrievalType FactRetrievalType { get; init; }
    public ClaritySynthesisType ClaritySynthesisType { get; init; }
    public bool HasCitation { get; init; }
    public double Confidence { get; init; } // 0–1 arbitration confidence
    public bool HasPronoun { get; set; }
    public bool IsGrammaticallyCorrect { get; set; }
    public bool IsPlagiarized { get; set; }
}

