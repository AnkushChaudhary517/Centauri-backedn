using Centauri.ContentArchitect.Backend.Models;

namespace Centauri.ContentArchitect.Backend.Services.Calculators;

public sealed class IndexabilityCalculator
{
    public IndexabilityResult Calculate(SiteIndexData x)
    {
        var readiness = x.IsEstimated
            ? MetricMath.Clamp(
                0.50 * x.HistoricalIndexRate +
                0.20 * x.CrawlHealth +
                0.15 * x.CanonicalConsistency +
                0.10 * x.SitemapHealth +
                0.05 * x.InternalDiscovery)
            : MetricMath.Clamp(
                0.40 * x.HistoricalIndexRate +
                0.20 * x.CrawlHealth +
                0.15 * x.CanonicalConsistency +
                0.10 * x.SitemapHealth +
                0.10 * x.InternalDiscovery +
                0.05 * x.DomainStrength);

        return new IndexabilityResult
        {
            ReadinessScore = readiness,
            Label = readiness <= 20 ? "Very Low" :
                    readiness <= 40 ? "Low" :
                    readiness <= 60 ? "Moderate" :
                    readiness <= 80 ? "High" : "Very High",
            IsProbability = false,
            IsEstimated = x.IsEstimated,
            Source = x.Source,
            Probability14Days = null
        };
    }
}
