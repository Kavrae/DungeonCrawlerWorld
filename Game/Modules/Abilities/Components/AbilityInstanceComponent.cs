namespace Game.Modules.Abilities.Components;

/// <summary>
/// One granted ability instance -- an entity's full set of abilities is "however many of these
/// it has" (MultiComponentPool, the RaceComponent/StatusEffectStack pattern), not a single
/// component holding a list. DamageAmount lives here, per instance, rather than on the shared
/// AbilityDefinition it points to via AbilityId -- multiple entities (e.g. every race/class's
/// "Default Attack") can share one catalog AbilityDefinition while each hitting for a different
/// amount (Player 20, Goblin 10, Fairy 5 -- see the race/PlayerBlueprint grants).
/// CooldownFramesRemaining is meaningful for any ability whose AbilityTiming.CooldownFrames is
/// set, regardless of ActionTimingCategory -- ticked by AbilityCooldownSystem.
/// </summary>
public struct AbilityInstanceComponent(Guid abilityId, short damageAmount, short cooldownFramesRemaining)
{
    public Guid AbilityId { get; } = abilityId;
    public short DamageAmount { get; set; } = damageAmount;
    public short CooldownFramesRemaining { get; set; } = cooldownFramesRemaining;

    public override readonly string ToString() => $"AbilityId : {AbilityId}\nDamageAmount : {DamageAmount}\nCooldownFramesRemaining : {CooldownFramesRemaining}";
}
