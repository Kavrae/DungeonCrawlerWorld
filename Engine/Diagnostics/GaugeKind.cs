namespace Engine.Diagnostics;

/// <summary>How a gauge's reading is interpreted when it is sampled.</summary>
/// <cleanupVersion>1</cleanupVersion>
public enum GaugeKind
{
    /// <summary>A value that means something on its own (a queue depth, an entity count); sampled as read.</summary>
    Level,

    /// <summary>A counter that only grows (collections, bytes allocated); sampled as its change since the previous frame.</summary>
    Cumulative,
}
