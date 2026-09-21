using Centauri.ContentArchitect.Backend.Models;

namespace Centauri.ContentArchitect.Backend.Services.Calculators;

public sealed class KeywordDifficultyCalculator
{
    public KeywordDifficultyResult Calculate(IReadOnlyList<SerpResult> top10)
    {
        var top = top10.Take(10).ToList();
        var ap = MetricMath.Mean(top.Select(x => MetricMath.Clamp(x.DomainStrength)));
        var lpValues = top.Select(x => MetricMath.Clamp(
            100.0 * Math.Log(1.0 + Math.Max(0, x.ReferringDomains)) /
            Math.Log(1.0 + 1000.0)));
        var lp = MetricMath.Median(lpValues);
        var isat = top.Count == 0 ? 0 : 100.0 * top.Count(x => x.IntentMatch >= 0.80) / 10.0;
        var ccs = MetricMath.Mean(top.Select(x => MetricMath.Clamp(x.ContentCoverage)));

        var kd = MetricMath.Clamp(
            0.35 * ap +
            0.35 * lp +
            0.20 * isat +
            0.10 * ccs);

        return new KeywordDifficultyResult
        {
            Score = kd,
            Label = kd <= 20 ? "Very Low" :
                    kd <= 40 ? "Low" :
                    kd <= 60 ? "Moderate" :
                    kd <= 80 ? "High" : "Very High",
            AuthorityPressure = ap,
            LinkPressure = lp,
            IntentSaturation = isat,
            CompetitorContentStrength = ccs
        };
    }
}
