namespace Engine.Diagnostics;

/// <summary>Forwards every Record to two recorders, so FrameRangeBenchmark and FrameBudgetTracker can both observe the same instrumentation.</summary>
/// <remarks>SystemManager, EventBus and ShellContext each hold a single IFrameCostRecorder; this keeps them unaware of how many consumers there are. See DiagnosticsEngine.FrameCostRecorder.</remarks>
/// <cleanupVersion>1</cleanupVersion>
internal sealed class CompositeFrameCostRecorder(IFrameCostRecorder first, IFrameCostRecorder second) : IFrameCostRecorder
{
    public void Record(FrameCostCategory category, string groupName, string itemName, TimeSpan elapsed)
    {
        first.Record(category, groupName, itemName, elapsed);
        second.Record(category, groupName, itemName, elapsed);
    }
}
