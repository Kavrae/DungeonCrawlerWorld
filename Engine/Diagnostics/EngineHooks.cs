namespace Engine.Diagnostics;

/// <summary>The named channels engine and game code emit diagnostics through, each with at most one listener.</summary>
/// <remarks>
/// An emit site reads a channel's Listener and does nothing when it is null, so a channel nobody
/// listens to costs a read and a branch. Nothing passes a recorder into the code it measures:
/// DiagnosticsEngine subscribes in Start and unsubscribes in Dispose. The channels are process-wide,
/// so a test that subscribes must not run in parallel with other tests.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class EngineHooks
{
    /// <summary>Per-frame wall-clock costs of systems, event dispatch and windows.</summary>
    public static readonly EngineHookChannel<IFrameCostRecorder> FrameCosts = new(nameof(FrameCosts));

    /// <summary>Times the work inside it onto the FrameCosts channel: `using (EngineHooks.FrameCost(FrameCostCategory.Draw, "GameLoop", "Shell.Draw")) { ... }`.</summary>
    /// <remarks>For code that runs a few times a frame. Its arguments are evaluated and a try/finally entered even with nothing listening, so a path that runs per system or per event branches on FrameCosts.Listener itself (SystemManager.Update, EventBus.Publish).</remarks>
    public static FrameCostHandle FrameCost(FrameCostCategory category, string groupName, string itemName) =>
        FrameCosts.Listener is { } frameCostRecorder ? new FrameCostHandle(frameCostRecorder, category, groupName, itemName) : default;

    /// <summary>The start and end of every simulation frame, emitted by SystemManager.Update.</summary>
    public static readonly EngineHookChannel<ISimulationFrameListener> SimulationFrames = new(nameof(SimulationFrames));

    /// <summary>The start and end of the simulated session, emitted by EcsContext.BeginSession and EcsContext.Dispose -- never by a build that is not simulated (a mod's trial build, a staging rebuild).</summary>
    public static readonly EngineHookChannel<ISimulationSessionListener> Sessions = new(nameof(Sessions));

    /// <summary>Named, nested spans of work: EcsBuilder's stages and each module's phase within them, and the host's own startup steps.</summary>
    public static readonly EngineHookChannel<IDiagnosticScopeListener> DiagnosticScopes = new(nameof(DiagnosticScopes));

    /// <summary>Opens a scope on the DiagnosticScopes channel, ended by disposing the result: `using (EngineHooks.DiagnosticScope("Entity Population")) { ... }`.</summary>
    /// <param name="name">A constant name.</param>
    /// <param name="detail">What the span runs for (a module's name), if the name alone doesn't say.</param>
    public static DiagnosticScopeHandle DiagnosticScope(string name, string? detail = null) =>
        DiagnosticScopes.Listener is { } scopeListener ? new DiagnosticScopeHandle(scopeListener, new DiagnosticScope(name, detail)) : default;
}
