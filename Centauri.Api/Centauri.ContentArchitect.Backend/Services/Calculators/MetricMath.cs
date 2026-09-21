namespace Centauri.ContentArchitect.Backend.Services.Calculators;

public static class MetricMath
{
    public static double Clamp(double value, double min = 0, double max = 100)
        => Math.Min(max, Math.Max(min, value));

    public static double Mean(IEnumerable<double> values)
    {
        var a = values.ToArray();
        return a.Length == 0 ? 0 : a.Average();
    }

    public static double Median(IEnumerable<double> values)
    {
        var a = values.OrderBy(x => x).ToArray();
        if (a.Length == 0) return 0;
        var mid = a.Length / 2;
        return a.Length % 2 == 1 ? a[mid] : (a[mid - 1] + a[mid]) / 2.0;
    }

    public static double WeightedMean(IEnumerable<(double Value, double Weight)> values)
    {
        var a = values.ToArray();
        var weight = a.Sum(x => x.Weight);
        return weight == 0 ? 0 : a.Sum(x => x.Value * x.Weight) / weight;
    }
}
