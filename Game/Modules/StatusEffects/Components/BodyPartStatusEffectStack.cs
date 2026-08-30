using Game.World;

namespace Game.Modules.StatusEffects.Components;

/// <summary>
/// One stack unit of one effect from one source, scoped to a specific body part -- mirrors
/// StatusEffectStack, plus a PartId. An entity holding N of these for the same (PartId, EffectType)
/// (in the MultiComponentPool this is registered as) has N stacks on that one part. Only Burning
/// grants these today (see BurningAuraApplier/BodyPartBurningSystem); registered alongside
/// StatusEffectStack in StatusEffectsModule as a shared cross-effect concern, not a Burning-specific one.
/// </summary>
public readonly record struct BodyPartStatusEffectStack(byte PartId, StatusEffectType EffectType, StatusEffectSource Source)
{
    public override readonly string ToString() => $"PartId : {PartId}\nEffectType : {EffectType}\nSource : {Source}";
}
