namespace Game.Modules.Inventory;

/// <summary>Something an item can't have done to it.</summary>
public enum ItemRestriction : byte
{
    /// <summary>It never leaves its owner: it can't be traded, looted, sold, bought, dropped or destroyed (ItemDefinition.CanTrade false).</summary>
    Untradable,
}

/// <summary>The restrictions an item carries, in the order they are shown -- the one place a new restriction (unsellable, undroppable, indestructible) is added.</summary>
public static class ItemRestrictions
{
    private static readonly ItemRestriction[] UntradableOnly = [ItemRestriction.Untradable];

    public static IReadOnlyList<ItemRestriction> Of(ItemDefinition item) => item.CanTrade ? [] : UntradableOnly;
}
