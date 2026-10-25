using System.Globalization;

namespace Engine.Diagnostics;

/// <summary>How a gauge's value is shown, by its unit: bytes in MB, milliseconds in ms, anything else as a count -- the one formatting every diagnostics view uses.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class GaugeValueFormatting
{
    private const double BytesPerMegabyte = 1024 * 1024;

    /// <summary>value in unit: "12.3 MB", "4.56 ms", "1,024", or "0.50" for a count that isn't whole.</summary>
    public static string Format(GaugeUnit unit, double value) => unit switch
    {
        GaugeUnit.Bytes => string.Create(CultureInfo.InvariantCulture, $"{value / BytesPerMegabyte:N1} MB"),
        GaugeUnit.Milliseconds => string.Create(CultureInfo.InvariantCulture, $"{value:N2} ms"),
        _ => value.ToString(value == System.Math.Floor(value) ? "N0" : "N2", CultureInfo.InvariantCulture),
    };

    /// <summary>value in the unit gaugeName's suffix names.</summary>
    public static string Format(string gaugeName, double value) => Format(GaugeUnits.Of(gaugeName), value);

    /// <summary>A window of a gauge's values in a few words: a counter's total over it, a level's range across it.</summary>
    public static string FormatSummary(GaugeKind kind, GaugeUnit unit, in GaugeWindowStatistics statistics) => kind is GaugeKind.Cumulative
        ? $"total {Format(unit, statistics.Total)}"
        : $"{Format(unit, statistics.Minimum)} - {Format(unit, statistics.Maximum)}";
}
