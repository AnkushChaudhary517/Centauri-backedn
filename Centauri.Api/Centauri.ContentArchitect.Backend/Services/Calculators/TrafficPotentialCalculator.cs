using Centauri.ContentArchitect.Backend.Configuration;
using Centauri.ContentArchitect.Backend.Models;
using Microsoft.Extensions.Options;

namespace Centauri.ContentArchitect.Backend.Services.Calculators;

public sealed class TrafficPotentialCalculator
{
    private readonly AnalysisOptions _options;
    public TrafficPotentialCalculator(IOptions<AnalysisOptions> options) => _options = options.Value;

    public TrafficPotentialResult Calculate(
        IReadOnlyList<KeywordCluster> clusters,
        double indexabilityReadiness,
        double serpClickability)
    {
        var ip = MetricMath.Clamp(indexabilityReadiness, 0, 100) / 100.0;
        var scenarios = new[] { ("Conservative", 12), ("Expected", 8), ("Strong", 5) };

        return new TrafficPotentialResult
        {
            Scenarios = scenarios.Select(s =>
            {
                var ctr = GetCtr(s.Item2);
                var traffic = clusters.Sum(c => c.Volume * ctr * serpClickability * ip);
                return new TrafficScenario
                {
                    Name = s.Item1,
                    Position = s.Item2,
                    EstimatedMonthlyTraffic = Math.Round(traffic, 0)
                };
            }).ToList()
        };
    }

    private double GetCtr(int position)
        => _options.CtrByPosition.TryGetValue(position.ToString(), out var ctr)
            ? ctr
            : Math.Max(0.005, 0.30 / Math.Pow(position, 0.85));
}
