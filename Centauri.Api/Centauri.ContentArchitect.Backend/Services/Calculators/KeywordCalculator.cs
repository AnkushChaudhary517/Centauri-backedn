using Centauri.ContentArchitect.Backend.Configuration;
using Centauri.ContentArchitect.Backend.Models;
using Microsoft.Extensions.Options;

namespace Centauri.ContentArchitect.Backend.Services.Calculators;

public sealed class KeywordCalculator : IKeywordCalculator
{
    private readonly AnalysisOptions _options;
    public KeywordCalculator(IOptions<AnalysisOptions> options) => _options = options.Value;

    public SearchVolumeResult Calculate(KeywordData keywordData)
    {
        // The specification requires semantic clustering and deduplication before summing.
        // The AI client performs clustering upstream; this calculator sums canonical clusters only.
        var demand = keywordData.PrimarySearchVolume +
                     keywordData.SecondaryClusters.Sum(x => x.Volume);

        return new SearchVolumeResult
        {
            PrimaryVolume = keywordData.PrimarySearchVolume,
            AddressableSearchDemand = demand,
            DeduplicatedClusters = keywordData.SecondaryClusters
        };
    }
}
