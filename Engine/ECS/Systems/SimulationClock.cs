namespace Engine.ECS.Systems;

/// <summary>The current simulation frame, readable outside a system's Update -- by queries, gates and Presentation.</summary>
/// <remarks>
/// Systems already have the frame (EngineTime.FrameCount); this exists for code that isn't handed
/// an EngineTime but needs "now" to read a deadline (see FrameDeadline) -- ActionActivationSystem's
/// helpers, the hotbar's cooldown fill, and so on.
///
/// SystemManager.Update advances it to the frame being simulated before any system runs, so during
/// frame F every reader sees F, and between frames (Presentation's Update/Draw) it still reads the
/// last simulated frame. It stops while the game is paused, because simulation frames do.
///
/// One clock for now (PLAN-timer-wheel.md, Decisions 3), deliberately passed explicitly to whoever
/// reads it rather than exposed as a static, so a per-map clock later is a wiring change.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class SimulationClock
{
    /// <summary>The simulation frame currently running, or last run. 0 before the first update.</summary>
    public long CurrentFrame { get; private set; }

    /// <summary>Sets the current frame. Called by SystemManager.Update; tests may call it directly to stage a frame.</summary>
    public void Advance(long frame) => CurrentFrame = frame;
}
