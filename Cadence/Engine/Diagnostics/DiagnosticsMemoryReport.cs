namespace Engine.Diagnostics;

/// <summary>A benchmark range's pool memory, as memory-*.json holds it: the process's allocation before the range and heap after, and every pool's size and churn -- written by PoolMemoryReport, read by DiagnosticsReportReader.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsMemoryReport(
    DateTime TimestampUtc,
    int? RandomSeed,
    int ProcessId,
    long StartFrame,
    long EndFrame,
    int LivingEntities,
    long AllocatedBytesBeforeRange,
    int Gen0CollectionsBeforeRange,
    int Gen1CollectionsBeforeRange,
    int Gen2CollectionsBeforeRange,
    long LiveHeapBytesAtEnd,
    long PeakWorkingSetBytes,
    long TotalPoolBytes,
    IReadOnlyList<PoolMemoryReportEntry> Pools);
