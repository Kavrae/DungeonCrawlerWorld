using Game.Admin;
using Game.Bootstrap;
using Game.Diagnostics;
using Game.Floors;

namespace DungeonCrawlerWorld;

/// <summary>The game session GameLoop simulates, plus the pieces around it that only this app owns.</summary>
/// <remarks>Disposing it ends the session: the EcsContext first (EngineHooks.Sessions hears it while the pools are intact), then the player activity log.</remarks>
public sealed record WorldSessionContext(
    GameSession GameSession,
    PlayerActivityLog PlayerActivityLog,
    AdminTools AdminTools,
    ReservedEntityIds ReservedEntityIds) : IDisposable
{
    public void Dispose()
    {
        GameSession.EcsContext.Dispose();
        PlayerActivityLog.Dispose();
    }
}
