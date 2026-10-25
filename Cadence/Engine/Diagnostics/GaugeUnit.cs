namespace Engine.Diagnostics;

/// <summary>What a gauge's value measures, read from its name's suffix.</summary>
/// <cleanupVersion>1</cleanupVersion>
public enum GaugeUnit
{
    /// <summary>A count, shown as a number.</summary>
    Count,

    /// <summary>A gauge named "...Bytes".</summary>
    Bytes,

    /// <summary>A gauge named "...Milliseconds".</summary>
    Milliseconds,
}
