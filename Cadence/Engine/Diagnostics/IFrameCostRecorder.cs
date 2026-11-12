namespace Engine.Diagnostics;

/// <summary>Opt-in per-frame wall-clock cost recording, by row: a category (Update/Draw), a group (e.g. "SystemManager", "EventBus", a window tier name) and an item (a system/event/window type name), named once by FrameCostRowIds.</summary>
/// <remarks>
/// Implemented by FrameBudgetTracker and FrameRangeBenchmark. Emit sites record through EngineHooks.FrameCost (a host's update and draw), or on the hottest paths branch on EngineHooks.FrameCosts.Listener themselves (SystemManager, EventBus); either way nothing is timed when it is null.
/// Every emit site calls Begin as its measurement opens and Record as it closes, so measurements nest: a recorder that keeps the open ones knows what each ran inside, and adds a row's time only for its outermost open measurement.
/// Begin runs before the measurement's start timestamp and Record after its elapsed is taken, so whatever a recorder does lands in the enclosing measurement, never its own: a recorder keeps both to a few array writes.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface IFrameCostRecorder
{
    /// <summary>A measurement of the row opened; its Record closes it. Nothing by default, for a recorder that doesn't track nesting.</summary>
    void Begin(FrameCostRowId row) { }

    void Record(FrameCostRowId row, TimeSpan elapsed);
}
