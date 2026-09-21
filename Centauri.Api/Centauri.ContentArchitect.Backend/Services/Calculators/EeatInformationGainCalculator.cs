using Centauri.ContentArchitect.Backend.Models;
using System.Collections.Generic;

namespace Centauri.ContentArchitect.Backend.Services.Calculators;

public sealed class EeatInformationGainCalculator
{
    public EeatResult Calculate(ContentAnalysisAggregate a)
    {
        var evidence = 100.0 * (
            0.25 * a.FirstHandEvidenceRate +
            0.20 * a.OriginalDataPrevalence +
            0.15 * a.SourceRequirement +
            0.15 * a.AuthorityPressure +
            0.15 * a.YmyLTopicSensitivity +
            0.10 * a.FreshnessRequirement);

        var informationGain = 100.0 * (
            0.40 * a.CompetitorRedundancy +
            0.25 * a.MissingQuestionCoverage +
            0.20 * a.MissingEntityCoverage +
            0.15 * a.MissingEvidenceCoverage);

        var composite = 0.60 * evidence + 0.40 * informationGain;

        return new EeatResult
        {
            EvidenceRequirement = evidence,
            InformationGainOpportunity = informationGain,
            CompositeScore = composite,
            EvidenceExpected = evidence >= 67 ? "High" : evidence >= 34 ? "Medium" : "Low",
            InformationGainExpected = informationGain >= 67 ? "High" : informationGain >= 34 ? "Medium" : "Low",
            RecommendedEvidenceTypes = new List<string>
            {
                "First-party benchmark data",
                "Expert quotation",
                "Product screenshots",
                "Real workflow example",
                "Comparison based on tested criteria"
            }
        };
    }
}
