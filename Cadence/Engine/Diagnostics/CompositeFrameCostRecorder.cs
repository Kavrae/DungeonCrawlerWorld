namespace Engine.Diagnostics;

/// <summary>Forwards every Record to two recorders, so FrameRangeBenchmark and FrameBudgetTracker can both observe the same instrumentation.</summary>
/// <remarks>EngineHooks.FrameCosts holds a single listener; this lets DiagnosticsEngine subscribe both through it.</remarks>
/// <cleanupVersion>1</cleanupVersion>
internal sealed class CompositeFrameCostRecorder(IFrameCostRecorder first, IFrameCostRecorder second) : IFrameCostRecorder
{
    public void Record(FrameCostCategory category, string groupName, string itemName, TimeSpan elapsed)
    {
        first.Record(category, groupName, itemName, elapsed);
        second.Record(category, groupName, itemName, elapsed);
    }
}
