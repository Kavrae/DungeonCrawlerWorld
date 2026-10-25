namespace Engine.Diagnostics;

/// <summary>Reads a gauge's unit from its name: "...Bytes" and "...Milliseconds" name their unit, anything else is a count.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class GaugeUnits
{
    public static GaugeUnit Of(string gaugeName)
    {
        if (gaugeName.EndsWith("Bytes", StringComparison.Ordinal))
        {
            return GaugeUnit.Bytes;
        }

        if (gaugeName.EndsWith("Milliseconds", StringComparison.Ordinal))
        {
            return GaugeUnit.Milliseconds;
        }

        return GaugeUnit.Count;
    }
}
