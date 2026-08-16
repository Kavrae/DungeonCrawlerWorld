namespace Game.Modules.Actions.Components;

/// <summary>
/// One granted action instance</summary>
/// <remarks>
/// An entity's full set of actions is "however many of these
/// it has" (MultiComponentPool, the RaceComponent/StatusEffectStack pattern), not a single
/// component holding a list. 
/// 
/// TEMPORARY DamageAmount lives here, per instance, rather than on the shared
/// ActionDefinition it points to via ActionId -- multiple entities (e.g. every race/class's
/// "Default Attack") can share one catalog ActionDefinition while each hitting for a different
/// amount (Goblin 10, Fairy 3, Ghost 5 -- see the race blueprints' grants). A DamageAmount of 0
/// (the default) means "no per-instance override" -- ActionEffectResolver passes null through
/// as ActionEffectContext.DamageOverride in that case, so the granted DirectDamage rolls its
/// own MinAmount..MaxAmount range instead of a flat value (see PlayerBlueprint's Punch grant, the
/// one race that opts into rolled variance rather than a fixed per-race number).
/// CooldownFramesRemaining is meaningful for any action whose ActionTiming.CooldownFrames is
/// set, regardless of ActionTimingCategory -- ticked by ActionCooldownSystem.
/// </remarks>
public struct ActionInstanceComponent(Guid actionId, ushort damageAmount, ushort cooldownFramesRemaining)
{
    public Guid ActionId { get; } = actionId;
    public ushort DamageAmount { get; set; } = damageAmount;
    public ushort CooldownFramesRemaining { get; set; } = cooldownFramesRemaining;

    public override readonly string ToString() => $"ActionId : {ActionId}\nDamageAmount : {DamageAmount}\nCooldownFramesRemaining : {CooldownFramesRemaining}";
}
