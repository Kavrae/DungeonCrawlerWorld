namespace Engine.Diagnostics;

/// <summary>Opt-in per-frame wall-clock cost recording, grouped by category (Update/Draw), then by group (e.g. "SystemManager", "EventBus", a window tier name), then by item (a system/event/window type name).</summary>
/// <remarks>Implemented by FrameBudgetTracker and FrameRangeBenchmark. Emit sites record through EngineHooks.FrameCost (ShellContext, GameLoop), or on the hottest paths branch on EngineHooks.FrameCosts.Listener themselves (SystemManager, EventBus); either way nothing is timed when it is null.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface IFrameCostRecorder
{
    void Record(FrameCostCategory category, string groupName, string itemName, TimeSpan elapsed);
}
