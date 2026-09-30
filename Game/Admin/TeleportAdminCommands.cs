using Engine.Math;
using Game.World;

namespace Game.Admin;

/// <summary>Admin Mode's "Teleport here" command: moves the player to a tile, however far away.</summary>
/// <remarks>A debugging tool over EntityTeleporter, not a gameplay path.</remarks>
public sealed class TeleportAdminCommands(EntityTeleporter teleporter, IPlayerQuery playerQuery)
{
    /// <summary>Whether the player could be teleported to destination right now.</summary>
    public bool CanTeleportPlayer(Vector3Int destination) => teleporter.CanTeleport(playerQuery.PlayerEntityId, destination);

    /// <summary>Teleports the player to destination; false when it can't be occupied.</summary>
    public bool TryTeleportPlayer(Vector3Int destination) => teleporter.TryTeleport(playerQuery.PlayerEntityId, destination);
}
