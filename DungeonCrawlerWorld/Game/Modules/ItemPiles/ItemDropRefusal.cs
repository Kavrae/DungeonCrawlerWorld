namespace Game.Modules.ItemPiles;

/// <summary>Why a drop can't go ahead, or None.</summary>
public enum ItemDropRefusal : byte
{
    None,

    /// <summary>The dropper is dead.</summary>
    DropperDead,

    /// <summary>The dropper doesn't hold the stack, or holds none of the currency.</summary>
    NothingToDrop,

    /// <summary>The item can never leave its owner (ItemDefinition.CanTrade).</summary>
    CannotTrade,

    /// <summary>The stack is winding up or has an activation pending.</summary>
    StackInUse,

    /// <summary>The tile is a wall, off the map, or beyond the dropper's own footprint and the tiles around it (ItemDrops.Targeting).</summary>
    OutOfReach,

    /// <summary>A standing shop is on the tile: items reach a shop only through its window.</summary>
    IntoShop,

    /// <summary>The corpse on the tile is reserved for someone else for now (LootRights).</summary>
    LootReserved,

    /// <summary>The container on the tile has no room for any of it.</summary>
    NoRoom,
}

/// <summary>The text shown for a refused drop.</summary>
public static class ItemDropRefusalText
{
    public static string Describe(ItemDropRefusal refusal) => refusal switch
    {
        ItemDropRefusal.None => string.Empty,
        ItemDropRefusal.DropperDead => "You can't drop anything while dead",
        ItemDropRefusal.NothingToDrop => "Nothing to drop",
        ItemDropRefusal.CannotTrade => "This item can't be dropped",
        ItemDropRefusal.StackInUse => "This item is in use",
        ItemDropRefusal.OutOfReach => "Too far away to drop there",
        ItemDropRefusal.IntoShop => "Trade with a shop through its window",
        ItemDropRefusal.LootReserved => "Someone else's loot, for now",
        ItemDropRefusal.NoRoom => "No room",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, null),
    };
}
