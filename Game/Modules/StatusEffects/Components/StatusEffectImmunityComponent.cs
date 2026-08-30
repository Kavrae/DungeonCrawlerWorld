namespace Game.Modules.StatusEffects.Components;

/// <summary>
/// One active immunity to EffectType -- an entity holding N of these (MultiComponentPool) is
/// immune to N distinct StatusEffectTypes. Checked by each effect's own ApplyStack chokepoint
/// (PoisonEffects.ApplyStack, BurningEffects.ApplyStack, BurningAuraApplier's body-part-scoped
/// path) before a new stack ever gets added -- a hard on/off gate, not a StatModifierComponent
/// scale, since "immune" means the stack never lands at all, not "lands but does nothing".
/// </summary>
public struct StatusEffectImmunityComponent(StatusEffectType effectType, ushort? remainingDurationFrames)
{
    public StatusEffectType EffectType { get; } = effectType;

    /// <summary>null means "never expires" -- StatusEffectImmunityExpirySystem skips ticking/removing an immunity at this value, mirroring StatModifierComponent.RemainingDurationFrames.</summary>
    public ushort? RemainingDurationFrames { get; set; } = remainingDurationFrames;

    public override readonly string ToString() => $"EffectType : {EffectType}\nRemainingDurationFrames : {RemainingDurationFrames}";
}
