namespace Engine.Diagnostics;

/// <summary>The minimum, maximum, total and average of a window of a gauge's values; all zero for an empty window.</summary>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct GaugeWindowStatistics(double Minimum, double Maximum, double Total, int Count)
{
    public double Average => Count == 0 ? 0 : Total / Count;

    public static GaugeWindowStatistics Of(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty)
        {
            return default;
        }

        var minimum = double.MaxValue;
        var maximum = double.MinValue;
        var total = 0d;
        foreach (var value in values)
        {
            minimum = System.Math.Min(minimum, value);
            maximum = System.Math.Max(maximum, value);
            total += value;
        }

        return new GaugeWindowStatistics(minimum, maximum, total, values.Length);
    }
}
