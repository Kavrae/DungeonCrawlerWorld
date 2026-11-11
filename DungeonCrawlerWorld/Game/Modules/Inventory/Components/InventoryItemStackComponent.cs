using Engine.ECS.Components;
using Engine.ECS.Systems;

namespace Game.Modules.Inventory.Components;

/// <summary>
/// One stack of identical items in an entity's inventory -- entities own zero or more of these
/// via a MultiComponentPool (see InventoryModule). Quantity is the "identical items grouped with a
/// count" requirement. Whether its units can be used now is the user's blocker (ActivationQueries.GetBlocker,
/// the Disabled status among them), never a flag on the stack.
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
/// <remarks>
/// CooldownReadyAtFrame is the units' own cooldown (ActionTiming.CooldownFrames): an item's rate limit belongs to
/// the unit, not its user, so a unit just used moves to a stack cooling until then, and units cool together only
/// when they end on the same frame. It is a scheduled timer: ItemCooldownSystem returns the stack's units to an
/// interchangeable ready stack when it ends. FrameDeadline.Never when the units aren't cooling down.
/// </remarks>
public struct InventoryItemStackComponent(Guid itemDefinitionId, ushort quantity, ItemDefinition? overrideDefinition = null, bool isDivergent = false, uint cooldownReadyAtFrame = FrameDeadline.Never) : IKeyedScheduledTimer
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

    public ItemDefinition? Override { get; set; } = overrideDefinition;

    public bool IsDivergent { get; set; } = isDivergent;

    /// <summary>The frame the units' cooldown ends, or FrameDeadline.Never when they aren't cooling down.</summary>
    public uint CooldownReadyAtFrame { get; set; } = cooldownReadyAtFrame;

    private uint _timerWheelMark;

    /// <summary>Whether the units are cooling down at frame now.</summary>
    public readonly bool IsCoolingDown(long now) => CooldownReadyAtFrame != FrameDeadline.Never && !FrameDeadline.IsReached(CooldownReadyAtFrame, now);

    /// <summary>Frames until the units' cooldown ends; 0 when they aren't cooling down.</summary>
    public readonly int CooldownFramesRemaining(long now) => CooldownReadyAtFrame == FrameDeadline.Never ? 0 : FrameDeadline.Remaining(CooldownReadyAtFrame, now);

    readonly int IKeyedScheduledTimer.TimerKey => (int)StackInstanceId;

    uint IScheduledTimer.NextTickFrame { readonly get => CooldownReadyAtFrame; set => CooldownReadyAtFrame = value; }

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() =>
        $"ItemDefinitionId : {ItemDefinitionId}\nStackInstanceId : {StackInstanceId}\nAcquiredSequence : {AcquiredSequence}\nQuantity : {Quantity}\nIsDivergent : {IsDivergent}\nCooldownReadyAtFrame : {CooldownReadyAtFrame}";
}
