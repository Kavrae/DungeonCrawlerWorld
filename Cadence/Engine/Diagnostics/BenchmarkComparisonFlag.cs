namespace Engine.Diagnostics;

/// <summary>Whether a benchmark comparison's row moved past its threshold, and which way.</summary>
/// <cleanupVersion>1</cleanupVersion>
public enum BenchmarkComparisonFlag
{
    None,
    Regression,
    Improvement,
}
