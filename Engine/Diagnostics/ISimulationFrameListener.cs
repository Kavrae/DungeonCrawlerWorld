namespace Engine.Diagnostics;

/// <summary>Listens to the EngineHooks.SimulationFrames channel.</summary>
/// <cleanupVersion>1</cleanupVersion>
public interface ISimulationFrameListener
{
    /// <summary>Frame frameCount is about to run; no system has run yet and the clock has not advanced.</summary>
    void SimulationFrameStarting(long frameCount);

    /// <summary>Frame frameCount has finished, including its frame-scoped buffer clears.</summary>
    /// <param name="elapsed">The wall-clock cost of the whole SystemManager.Update.</param>
    void SimulationFrameEnded(long frameCount, TimeSpan elapsed);
}
