using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.ItemPiles;
using Presentation.UI.Inventory;

namespace Tests;

/// <summary>Records the drops an item window arms or places, accepting them all unless told not to.</summary>
internal sealed class TestPlayerItemDrops : IPlayerItemDrops
{
    public List<(ItemDrop Drop, HotkeySlot? Slot)> ArmedDrops { get; } = [];

    public List<(ItemDrop Drop, Vector3Int Tile)> PlacedDrops { get; } = [];

    /// <summary>Whether CanArmDrop, CanDropAt and TryDropAt accept; true by default.</summary>
    public bool AcceptsDrops { get; set; } = true;

    public bool CanArmDrop(ItemDrop drop) => AcceptsDrops;

    public void ArmDrop(ItemDrop drop, HotkeySlot? slot = null) => ArmedDrops.Add((drop, slot));

    public bool CanDropAt(ItemDrop drop, Vector3Int tile) => AcceptsDrops;

    public bool TryDropAt(ItemDrop drop, Vector3Int tile)
    {
        if (!AcceptsDrops)
        {
            return false;
        }

        PlacedDrops.Add((drop, tile));
        return true;
    }
}
