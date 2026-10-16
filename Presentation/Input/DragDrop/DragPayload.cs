using Game.Modules.Actions;
using Game.Modules.Currency;

namespace Presentation.Input.DragDrop;

/// <summary>Where a game drag was picked up.</summary>
public enum DragPayloadOrigin : byte
{
    InventoryCell,
    Currency,
    HotbarSlot,
}

/// <summary>What a game content drag carries: an item stack, a Merged Stack, an action or a currency, and where it was picked up.</summary>
/// <remarks>
/// Exactly one of ItemStackInstanceId, MergedItemDefinitionId, ActionId and CurrencyType is set. A Merged Stack has no single
/// stack, only the item every member shares (see InventoryItemStackCell's own doc comment for the Base/Diverging/Merged
/// vocabulary): it can move between inventories as a batch but never binds to a hotbar slot. OriginEntityId is the inventory
/// or currency row's entity, null for a hotbar slot (always the player's). OriginSlot is the hotbar slot a bound
/// item or action was dragged off, unbound on release wherever the drag ends. CanStageInTrade is the cell's, read only while
/// a shop is open.
/// </remarks>
public sealed record DragPayload(
    DragPayloadOrigin Origin,
    uint? ItemStackInstanceId = null,
    Guid? MergedItemDefinitionId = null,
    Guid? ActionId = null,
    CurrencyType? CurrencyType = null,
    int? OriginEntityId = null,
    bool CanStageInTrade = false,
    HotkeySlot? OriginSlot = null)
{
    /// <summary>Whether this is anything a hotbar slot could hold -- not a Merged Stack, not currency.</summary>
    public bool CanBindToHotbar => MergedItemDefinitionId is null && CurrencyType is null;
}
