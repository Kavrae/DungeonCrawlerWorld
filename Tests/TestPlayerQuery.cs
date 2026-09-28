using Engine.ECS.Entities;
using Game.World;

namespace Tests;

/// <summary>A fixed player for tests that don't build a World to ask.</summary>
/// <remarks>The player's key is TestSources.KeyOf(playerEntityId), matching TestSources.Entity(playerEntityId).</remarks>
internal sealed class TestPlayerQuery(int playerEntityId) : IPlayerQuery
{
    /// <summary>No player: an id no entity has, and so EntityKey.None.</summary>
    public static TestPlayerQuery NoPlayer { get; } = new(-1);

    public int PlayerEntityId { get; } = playerEntityId;

    public EntityKey PlayerEntityKey { get; } = TestSources.KeyOf(playerEntityId);
}
