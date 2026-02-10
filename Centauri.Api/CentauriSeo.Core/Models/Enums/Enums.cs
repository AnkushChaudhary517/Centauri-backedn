namespace CentauriSeo.Core.Models.Enums
{
    public enum SentenceStructure
    {
        Simple,
        Compound,
        Complex,
        CompoundComplex,
        Fragment
    }
    public enum VoiceType
    {
        Active,
        Passive,
        Both
    }
    public enum SourceType
    {
        Unknown,
        FirstParty,
        SecondParty,
        ThirdParty
    }
    public enum InformativeType
    {
        Fact,
        Claim,
        Definition,
        Opinion,
        Prediction,
        Statistic,
        Observation,
        Suggestion,
        Question,
        Transition,
        Filler,
        Uncertain
    }

    public enum InfoQuality
    {
        WellKnown,
        PartiallyKnown,
        Derived,
        Unique,
        False
    }

    public enum  FunctionalType
    {
        Declarative,
        Interrogative,
        Imperative,
        Exclamatory
    }

    public enum ClaritySynthesisType
    {
        Focused,
        ModerateComplexity,
        LowClarity,
        UnIndexable
    }
    public enum FactRetrievalType
    {
        VerifiableIsolated,
        ContexualMixed,
        Unverifiable,
        NotFactual
    }
}
