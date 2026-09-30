using Game.Bootstrap;
using Game.Floors;

namespace Game.Admin;

/// <summary>Every Admin Mode command, built once per session.</summary>
/// <remarks>Each tool here is a command set the Admin Mode context menu offers today and a future console can register the same way.</remarks>
public sealed class AdminTools(GameSession gameSession, NeighborhoodStreamer neighborhoodStreamer)
{
    public BlueprintAdminCommands BlueprintAdminCommands { get; } = new(gameSession.Internals.Factory, gameSession.Catalogs.Definitions);

    public LootboxAdminCommands LootboxAdminCommands { get; } = new(gameSession.EcsContext.ComponentManager, gameSession.Catalogs.LootboxCatalog, gameSession.EcsContext.EventBus);

    public NeighborhoodAdminCommands NeighborhoodAdminCommands { get; } = new(neighborhoodStreamer);

    public TeleportAdminCommands TeleportAdminCommands { get; } = new(gameSession.Internals.Teleporter, gameSession.World);
}
