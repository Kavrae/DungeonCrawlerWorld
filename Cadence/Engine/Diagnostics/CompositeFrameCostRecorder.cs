namespace Engine.Diagnostics;

/// <summary>Forwards every Begin and Record to two recorders, so FrameRangeBenchmark and FrameBudgetTracker can both observe the same instrumentation.</summary>
/// <remarks>EngineHooks.FrameCosts holds a single listener; this lets DiagnosticsEngine subscribe both through it.</remarks>
/// <cleanupVersion>1</cleanupVersion>
internal sealed class CompositeFrameCostRecorder(IFrameCostRecorder first, IFrameCostRecorder second) : IFrameCostRecorder
{
    public void Begin(FrameCostRowId row)
    {
        first.Begin(row);
        second.Begin(row);
    }

    public void Record(FrameCostRowId row, TimeSpan elapsed)
    {
        first.Record(row, elapsed);
        second.Record(row, elapsed);
    }
}
