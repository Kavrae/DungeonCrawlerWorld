using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.ItemPiles;

namespace Presentation.UI.Inventory;

/// <summary>The player putting items and currency down: arming a drop through ActionTargetingController, and placing it at once through IItemDropPlacement.</summary>
/// <remarks>
/// One rule (Game's ItemDrops) for every way to put something down, so a menu Drop confirmed on a tile and a drag released
/// there do the same thing: into a corpse or a container on any of its footprint tiles, onto the ground otherwise. A drop
/// takes no action lock and lands at once, even with the simulation paused behind an open inventory: putting things down is
/// inventory management, which a lock would only make tedious. A confirmed armed drop (ActionTargetingController) and a drag
/// onto the map (TryDropAt) both land through the same placement. Nothing is dropped while a shop is open: a trade decides
/// where items go until it closes.
/// </remarks>
public sealed class PlayerItemDrops(IItemDropPlacement placement, ActionTargetingController actionTargeting) : IPlayerItemDrops
{
    public bool CanArmDrop(ItemDrop drop) => actionTargeting.CanArmDrop(drop);

    public void ArmDrop(ItemDrop drop, HotkeySlot? slot = null) => actionTargeting.ArmDrop(drop, slot);

    public bool CanDropAt(ItemDrop drop, Vector3Int tile) => placement.CanDropAt(drop, tile);

    public bool TryDropAt(ItemDrop drop, Vector3Int tile) => placement.TryDropAt(drop, tile);
}
