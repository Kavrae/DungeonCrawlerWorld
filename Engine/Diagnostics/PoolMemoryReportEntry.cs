namespace Engine.Diagnostics;

/// <summary>One component pool's memory and value churn across a benchmark range, as measured by PoolMemoryReport.</summary>
/// <param name="DistinctValues">Distinct component values held when the range opened.</param>
/// <param name="HoldersAtStart">Entities holding at least one of this component when the range opened.</param>
/// <param name="SurvivingHolders">Of HoldersAtStart, those still alive (same EntityKey) when the range closed.</param>
/// <param name="ChangedHolders">Of SurvivingHolders, those whose values (as a multiset, for a Multi pool) differ at the close.</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct PoolMemoryReportEntry(
    string ComponentType,
    string PoolKind,
    int ComponentSize,
    bool HoldsReferences,
    int Count,
    long EstimatedBytes,
    int DistinctValues,
    int HoldersAtStart,
    int SurvivingHolders,
    int ChangedHolders);
