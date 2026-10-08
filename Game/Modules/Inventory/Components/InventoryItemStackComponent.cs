using Game.Modules.Inventory;

namespace Game.Modules.Inventory.Components;

/// <summary>
/// One stack of identical items in an entity's inventory -- entities own zero or more of these
/// via a MultiComponentPool (see InventoryModule). IsDisabled marks this specific stack as
/// unavailable (e.g. a starting item withheld until some later trigger) -- distinct from
/// InventoryDisabledComponent, which disables an entity's whole inventory. Quantity is the
/// "identical items grouped with a count" requirement.
///
/// StackInstanceId is a stable per-stack identity assigned once, at construction, from a session
/// counter -- an addressing key only (like an entity id), not simulation state, so it carries no
/// determinism requirement. 0 means no stack. It's what a hotkey binding or an in-flight activation
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
/// (see InventoryActions.AddDivergentItem/MoveOneUnit) -- the mechanism this
/// component's own doc comment used to only predict ("a stack that later diverges from its
/// ItemDefinition ... is expected to become its own Quantity == 1 stack once that system exists").
///
/// AcquiredSequence is stamped once, at construction, the same "assigned inline, not a ctor
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
    /// <summary>Which item this is, as an interned 2-byte handle (see ItemIds).</summary>
    public ushort ItemId { get; } = ItemIds.IdFor(itemDefinitionId);

    /// <summary>The item definition id ItemId stands for -- what every caller still reads and compares.</summary>
    public readonly Guid ItemDefinitionId => ItemIds.DefinitionIdOf(ItemId);

    /// <summary>Hands out the next stack instance id. A session counter rather than a Guid: this is an addressing key with no determinism requirement (see this component's own doc comment), and 16 bytes per stack bought nothing over 4.</summary>
    private static uint NextStackInstanceId() => (uint)System.Threading.Interlocked.Increment(ref _lastStackInstanceId);

    /// <summary>Hands out the next acquisition sequence number. Its own counter, because TryTransferStack re-stamps a looted stack without changing its identity.</summary>
    public static uint NextAcquiredSequence() => (uint)System.Threading.Interlocked.Increment(ref _lastAcquiredSequence);

    private static int _lastAcquiredSequence;

    /// <summary>0 is "no stack", so the first id handed out is 1.</summary>
    private static int _lastStackInstanceId;

    public uint StackInstanceId { get; } = NextStackInstanceId();

    /// <summary>When this stack was acquired, as a session counter rather than a timestamp: the "recently acquired" sort only needs the order, and a counter is 4 bytes to a timestamp's 8 with no clock read per stack.</summary>
    public uint AcquiredSequence { get; set; } = NextAcquiredSequence();

    public ushort Quantity { get; set; } = quantity;

    public bool IsDisabled { get; set; } = isDisabled;

    public ItemDefinition? Override { get; set; } = overrideDefinition;

    public bool IsDivergent { get; set; } = isDivergent;

    public override readonly string ToString() =>
        $"ItemDefinitionId : {ItemDefinitionId}\nStackInstanceId : {StackInstanceId}\nAcquiredSequence : {AcquiredSequence}\nQuantity : {Quantity}\nIsDisabled : {IsDisabled}\nIsDivergent : {IsDivergent}";
}
