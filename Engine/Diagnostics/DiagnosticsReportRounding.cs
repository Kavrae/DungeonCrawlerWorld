namespace Engine.Diagnostics;

/// <summary>Rounds a measured value for a written report, so floating-point noise doesn't reach the file.</summary>
/// <cleanupVersion>1</cleanupVersion>
internal static class DiagnosticsReportRounding
{
    private const int ReportDecimalPlaces = 3;

    public static double RoundForReport(double value) => System.Math.Round(value, ReportDecimalPlaces);

    /// <summary>Formats value for a text report: a whole number without decimals, anything else rounded like RoundForReport.</summary>
    public static string FormatForReport(double? value)
    {
        var roundedValue = RoundForReport(value ?? 0);
        return roundedValue == System.Math.Floor(roundedValue) ? roundedValue.ToString("N0") : roundedValue.ToString("N3");
    }
}
