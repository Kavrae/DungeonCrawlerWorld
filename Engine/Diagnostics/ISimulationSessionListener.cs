using Engine.ECS.Context;

namespace Engine.Diagnostics;

/// <summary>Listens to the EngineHooks.Sessions channel.</summary>
/// <cleanupVersion>1</cleanupVersion>
public interface ISimulationSessionListener
{
    /// <summary>The host has built session and is about to simulate it.</summary>
    void SessionStarted(EcsContext session);

    /// <summary>Session is being disposed; its pools are still intact.</summary>
    void SessionEnding(EcsContext session);
}
