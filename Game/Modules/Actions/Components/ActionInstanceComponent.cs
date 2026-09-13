namespace Game.Modules.Actions.Components;

/// <summary>
/// One granted action instance</summary>
/// <remarks>
/// An entity's full set of actions is "however many of these
/// it has" (MultiComponentPool, the RaceComponent/StatusEffectStack pattern), not a single
/// component holding a list.
///
/// Override lives here, per instance, rather than on the shared ActionDefinition it points to
/// via ActionId -- multiple entities (e.g. every race/class's "Default Attack") can share one
/// catalog ActionDefinition while each hitting for a different amount (Goblin 10, Fairy 3, Ghost
/// 5 -- see the race blueprints' grants) or diverging in any other way (targeting, ManaCost,
/// Tags). Mirrors InventoryItemStackComponent.Override's shape exactly: a full, nullable clone
/// of the catalog definition built via `with`, resolved by ActionInstanceQueries.
/// TryResolveEffectiveAction (Override if set, else the plain catalog lookup by ActionId) --
/// null means "no per-instance override," so the granted action's own catalog Effects apply
/// unmodified (e.g. PlayerBlueprint's Punch grant, which rolls DirectDamage's own
/// MinFlatDamage..MaxFlatDamage range rather than a fixed number).
///
/// CooldownReadyAtFrame is a deadline (FrameDeadline), meaningful for any action whose
/// ActionTiming.CooldownFrames is set, regardless of ActionTimingCategory. Nothing ticks it: it is
/// written once when the action is used (ActionInstanceQueries.TrySetCooldown) and read against
/// the current frame (ActionInstanceQueries.IsOnCooldown/CooldownFramesRemaining). It replaced a
/// frames-remaining countdown that ActionCooldownSystem walked down every stripe visit -- 0.1
/// ms/frame spent almost entirely confirming that no cooldown was running (PLAN-timer-wheel.md,
/// "ActionLock vs ActionCooldown, measured"). A new grant starts at 0: ready immediately.
/// </remarks>
public struct ActionInstanceComponent(Guid actionId, ActionDefinition? overrideDefinition)
{
    public Guid ActionId { get; } = actionId;
    public ActionDefinition? Override { get; set; } = overrideDefinition;

    /// <summary>The simulation frame from which this action can be used again. 0 (the default) = ready now.</summary>
    public uint CooldownReadyAtFrame { get; set; }

    public override readonly string ToString() => $"ActionId : {ActionId}\nOverride : {(Override is null ? "none" : Override.Name)}\nCooldownReadyAtFrame : {CooldownReadyAtFrame}";
}
