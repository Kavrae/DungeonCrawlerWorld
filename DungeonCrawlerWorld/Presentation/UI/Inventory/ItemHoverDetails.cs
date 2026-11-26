using Game.Modules.Inventory;
using Presentation.UI.Details;
using UIEngine.UI;
using UIEngine.UI.Content;

namespace Presentation.UI.Inventory;

/// <summary>An item cell under the pointer: whose inventory it is in, the stack (none for a merged cell) and the item shown.</summary>
public readonly record struct ItemHoverTarget(int EntityId, uint? StackInstanceId, ItemDefinition Definition) : IHoverTarget<ItemHoverTarget>
{
    public string Name => Definition.Name;

    /// <summary>Whether this names the same cell as other -- the same stack, or for a merged cell the same item in the same inventory.</summary>
    public bool IsSameCellAs(in ItemHoverTarget other) =>
        EntityId == other.EntityId &&
        StackInstanceId == other.StackInstanceId &&
        (StackInstanceId is not null || Definition.Id == other.Definition.Id);
}

/// <summary>Where an item's shop pricing comes from while a shop is open -- the grid the item was hovered in, which knows whether it is the shop's side and which stock it prices against.</summary>
public interface IItemShopPricingSource
{
    /// <summary>The pricing rows for target, or null when no shop is open; the same list instance while nothing it is built from changes.</summary>
    IReadOnlyList<TooltipRow>? ShopPricingRowsFor(in ItemHoverTarget target);

    /// <summary>The open shop's flat buy and sell prices for target -- before its stock moves them (ShopActions.ComputeBuyPrice/ComputeSellPrice) -- or null when no shop is open.</summary>
    (int Buy, int Sell)? ReferencePricesFor(in ItemHoverTarget target);
}

/// <summary>The Item Details popup as hovering sees it, priced by the grid an item was hovered in.</summary>
public interface IItemDetailsPopup : IDetailsPopup<ItemHoverTarget, IItemShopPricingSource>
{
    /// <summary>Starts compare mode on entityId's stack, priced by shopPricing while a shop is open -- an item cell's "Compare".</summary>
    void Compare(int entityId, uint stackInstanceId, IItemShopPricingSource shopPricing);
}

/// <summary>What hovering an item cell shows (HoverDetails): its name above the cursor at once, then Item Details at the cursor after the delay.</summary>
public sealed class ItemHoverDetails(CursorTextContent cursorLabel) : HoverDetails<ItemHoverTarget, IItemShopPricingSource, IItemDetailsPopup>(cursorLabel)
{
    /// <summary>How long an item cell is hovered before Item Details opens (HoverDetailsTiming).</summary>
    public static int DetailsDelayFrames => HoverDetailsTiming.DetailsDelayFrames;

    /// <summary>An item cell's "Compare": starts compare mode on entityId's stack, priced by the grid it was chosen in.</summary>
    public void Compare(int entityId, uint stackInstanceId, IItemShopPricingSource shopPricing) => Popup?.Compare(entityId, stackInstanceId, shopPricing);
}
