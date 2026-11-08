using Engine.Math;
using Game.World;

namespace Game.Modules.ItemPiles;

/// <summary>The player putting items and currency down, for code outside a system.</summary>
/// <remarks>A drop happens at once, whatever the action lock and whether or not the simulation is paused: putting things down is inventory management, which a lock would only make tedious.</remarks>
public sealed class ItemDropCommands(ItemDrops itemDrops, IPlayerQuery playerQuery)
{
    /// <inheritdoc cref="ItemDrops.GetRefusal(int, in ItemDrop)"/>
    public ItemDropRefusal GetRefusal(ItemDrop drop) => itemDrops.GetRefusal(playerQuery.PlayerEntityId, in drop);

    /// <inheritdoc cref="ItemDrops.GetRefusal(int, in ItemDrop, Vector3Int)"/>
    public ItemDropRefusal GetRefusal(ItemDrop drop, Vector3Int tile) => itemDrops.GetRefusal(playerQuery.PlayerEntityId, in drop, tile);

    /// <summary>Drops drop from the player onto tile: into the container there, or onto the ground.</summary>
    /// <returns>None when it was dropped; otherwise why not, with nothing changed.</returns>
    public ItemDropRefusal TryDrop(ItemDrop drop, Vector3Int tile) => itemDrops.TryDrop(playerQuery.PlayerEntityId, in drop, tile);
}
