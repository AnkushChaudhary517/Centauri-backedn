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
        Passive
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
}
