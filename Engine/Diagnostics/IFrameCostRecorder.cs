namespace Engine.Diagnostics;

/// <summary>Opt-in per-frame wall-clock cost recording, grouped by category (Update/Draw), then by group (e.g. "SystemManager", "EventBus", a window tier name), then by item (a system/event/window type name).</summary>
/// <remarks>Implemented by FrameBudgetTracker. Callers (SystemManager, EventBus, GameShellContext) hold this as a nullable property/parameter and skip the Stopwatch entirely when null -- see FrameBudgetTracker's own doc comment.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface IFrameCostRecorder
{
    void Record(FrameCostCategory category, string groupName, string itemName, TimeSpan elapsed);
}
