using Game.Modules.Inventory;

namespace Game.Modules.Inventory.Components;

/// <summary>
/// One stack of identical items in an entity's inventory -- entities own zero or more of these
/// via a MultiComponentPool (see InventoryModule). IsDisabled marks this specific stack as
/// unavailable (e.g. a starting item withheld until some later trigger) -- distinct from
/// InventoryDisabledComponent, which disables an entity's whole inventory. Quantity is the
/// "identical items grouped with a count" requirement.
///
/// StackInstanceId is a stable per-stack identity assigned once, at construction, via
/// Guid.NewGuid() -- an addressing key only (like an entity id), not simulation state, so it
/// carries no determinism requirement. It's what a hotkey binding or an in-flight activation
/// references, so it survives this stack's own Quantity/Override changing underneath it later
/// (see InventoryQueries.TryFindByStackInstanceId).
///
/// Override, when set, IS this stack's effective ItemDefinition -- built via an ordinary `with`
/// off the catalog original (see InventoryQueries.TryResolveEffectiveItem) -- instead of always
/// resolving through ItemCatalog by ItemDefinitionId. IsDivergent is a separate flag from "Override
/// is set": a freshly-granted batch of otherwise-identical items can carry an Override too (e.g.
/// Wand of Fireball's Intelligence-derived MaxCharges, baked in once at grant time) without yet
/// being divergent -- every unit in that batch is still identical to every other. A stack only
/// becomes IsDivergent once a specific unit is actually used/altered and peeled off from its batch
/// (see InventoryActions.AddDivergentItem/PeelOneIntoDivergentStack) -- the mechanism this
/// component's own doc comment used to only predict ("a stack that later diverges from its
/// ItemDefinition ... is expected to become its own Quantity == 1 stack once that system exists").
///
/// FirstAcquiredUtcTicks is stamped once, at construction, the same "assigned inline, not a ctor
/// param" shape as StackInstanceId above -- every call site that builds a genuinely new stack
/// (InventoryActions.AddItem/AddItemWithOverride/AddDivergentItem) gets a fresh timestamp for
/// free, with no explicit code at any of them. Merging into an existing stack (plain or already-
/// divergent) only ever mutates that stack's Quantity in place, so its original timestamp is
/// never touched -- and InventoryActions.TryTransferStack moves a stack by copying this whole
/// struct verbatim, so a transferred stack normally keeps the timestamp it already had. It has a
/// setter (unlike the otherwise-identical-shaped StackInstanceId, which never changes) for exactly
/// one exception: TryTransferStack re-stamps it to "now" when the destination is the player --
/// looting an item into the player's own inventory reads as a fresh acquisition for sort purposes,
/// regardless of how long it sat in a corpse or another entity's inventory first. Powers the
/// "recently acquired" sort (see InventorySortOrder.RecentlyAcquiredDescending).
/// </summary>
public struct InventoryItemStackComponent(Guid itemDefinitionId, ushort quantity, bool isDisabled = false, ItemDefinition? overrideDefinition = null, bool isDivergent = false)
{
    public Guid ItemDefinitionId { get; } = itemDefinitionId;

    public Guid StackInstanceId { get; } = Guid.NewGuid();

    public long FirstAcquiredUtcTicks { get; set; } = DateTime.UtcNow.Ticks;

    public ushort Quantity { get; set; } = quantity;

    public bool IsDisabled { get; set; } = isDisabled;

    public ItemDefinition? Override { get; set; } = overrideDefinition;

    public bool IsDivergent { get; set; } = isDivergent;

    public override readonly string ToString() =>
        $"ItemDefinitionId : {ItemDefinitionId}\nStackInstanceId : {StackInstanceId}\nFirstAcquiredUtcTicks : {FirstAcquiredUtcTicks}\nQuantity : {Quantity}\nIsDisabled : {IsDisabled}\nIsDivergent : {IsDivergent}";
}
