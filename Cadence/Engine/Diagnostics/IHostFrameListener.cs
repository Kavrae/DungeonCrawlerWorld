namespace Engine.Diagnostics;

/// <summary>Listens to the EngineHooks.HostFrames channel.</summary>
/// <cleanupVersion>1</cleanupVersion>
public interface IHostFrameListener
{
    /// <summary>The host finished one update; simulated is false when it ran no simulation frame (paused, or a menu holding it).</summary>
    void HostUpdated(bool simulated);

    /// <summary>The host drew one frame.</summary>
    void HostDrew();
}
