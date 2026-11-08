using Game.Modules.Actions;
using Game.Modules.ItemPiles;

namespace Presentation.UI.Inventory;

/// <summary>The player putting items and currency down: armed from an item window's "Drop" and "Drop All", or dragged straight onto a tile.</summary>
public interface IPlayerItemDrops : IItemDropPlacement
{
    /// <summary>Whether drop can be armed now.</summary>
    bool CanArmDrop(ItemDrop drop);

    /// <summary>Arms drop, awaiting the tile it lands on.</summary>
    /// <param name="slot">The hotbar slot it came from, if any.</param>
    void ArmDrop(ItemDrop drop, HotkeySlot? slot = null);
}
