using Engine.Math;
using Game.Modules.Inventory;

namespace Game.Modules.ItemPiles;

/// <summary>Drops one unit on a tile through the one drop rule (ItemDrops): into the container or pile there, or a new pile.</summary>
/// <remarks>What a toggle item lit in Ground mode is put down with (ToggleItemGroundPlacement), so it lands exactly where a Drop of it there would, refused where that would be.</remarks>
internal sealed class ItemDropGroundPlacer(ItemDrops itemDrops) : IItemGroundPlacer
{
    public bool TryPutDownOneUnitAt(int holderEntityId, uint stackInstanceId, Vector3Int tile) =>
        itemDrops.TryDrop(holderEntityId, ItemDrop.OneUnitOf(stackInstanceId), tile) == ItemDropRefusal.None;
}
