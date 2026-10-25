namespace Workshop.Debug;

/// <summary>What a file in a report folder holds, read from its name.</summary>
public enum DiagnosticsReportKind
{
    /// <summary>latest.json: the periodic report.</summary>
    Periodic,

    /// <summary>benchmark-*.json: one benchmark range's frame costs.</summary>
    Benchmark,

    /// <summary>memory-*.json: one benchmark range's pool memory.</summary>
    Memory,

    /// <summary>gauges-*.json: one benchmark range's gauges, frame by frame.</summary>
    Gauges,

    /// <summary>startup-*.json: the startup scopes.</summary>
    Startup,

    /// <summary>A phase-performance-testing run the skill saved (its medians): what a later run is compared with.</summary>
    SavedRun,

    /// <summary>ab-*.json: the skill's A/B of a baseline build against the current one.</summary>
    AbComparison,
}
