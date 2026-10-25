namespace Engine.Diagnostics;

/// <summary>Opt-in per-frame wall-clock cost recording, grouped by category (Update/Draw), then by group (e.g. "SystemManager", "EventBus", a window tier name), then by item (a system/event/window type name).</summary>
/// <remarks>
/// Implemented by FrameBudgetTracker and FrameRangeBenchmark. Emit sites record through EngineHooks.FrameCost (a host's update and draw), or on the hottest paths branch on EngineHooks.FrameCosts.Listener themselves (SystemManager, EventBus); either way nothing is timed when it is null.
/// Every emit site calls Begin as its measurement opens and Record as it closes, so measurements nest: a recorder that keeps the open ones (FrameBudgetTracker) knows what each ran inside.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface IFrameCostRecorder
{
    /// <summary>A measurement of the item opened; its Record closes it. Nothing by default, for a recorder that doesn't track nesting.</summary>
    void Begin(FrameCostCategory category, string groupName, string itemName) { }

    void Record(FrameCostCategory category, string groupName, string itemName, TimeSpan elapsed);
}
