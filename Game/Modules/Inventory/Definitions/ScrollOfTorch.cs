using Engine.Math;
using Engine.Utilities;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory.Definitions;

/// <summary>
/// Second concrete ScrollActivator item -- proves ScrollScalingEffects (Range/AreaSize/duration
/// all scale together off the caster's Intelligence, 100% at 1 up to 400% at 300) and the
/// mastery-*synthesis* half of ScrollMasteryEffects: SpellId is a brand-new Guid with no existing
/// ActionDefinition, so mastering this scroll builds and registers a fresh "Torch" spell at
/// runtime instead of looking one up.
///
/// Effect is AuraSourceGrant's timed mode granting StatusEffectType.Light -- a purely
/// cosmetic map glow today (MapWindow renders it generically via MapTintGrid, the same pipeline
/// Lava's own Burning glow already uses, with zero Torch-specific knowledge anywhere in
/// Presentation). AuraAndGlowStrength: 8 matches this scroll's own base AreaSize (3) via
/// DistanceFalloff.MaxRadius(strength) = log2(strength), the same way Lava's Strength 8 produces
/// a 3-tile range -- fixed, not itself re-derived from the scaled targeting AreaSize (only
/// Duration is explicitly scaled here, same as every other scroll effect entry). Future TODO:
/// reveal fog of war in its AOE and damage entities with a light weakness (vampires) -- register a
/// real IStatusEffectAuraApplier for StatusEffectType.Light once that lands; nothing here needs to
/// change to support it (see StatusEffectType.Light's own doc comment).
/// </summary>
public static class ScrollOfTorch
{
    public static readonly Guid Id = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000021");
    public static readonly Guid SpellId = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000022");

    private const int MaximumStackSize = 999;
    private const int BaseFramesRemaining = GameTiming.FramesPerSecond * 10; // 10s at Intelligence 1 (100%)
    private const int AuraAndGlowStrength = 8; // -> 3-tile reach, matching this scroll's own base AreaSize

    public static ItemDefinition Build() => new(
        Id, "Scroll of Torch", "Scroll", "t", Color.White,
        Tags: [Tag.Scroll, Tag.Consumable, Tag.Self],
        Effects: [new ActionEffect([new AuraSourceGrant(StatusEffectType.Light, AuraAndGlowStrength, Color.White, DurationFrames: BaseFramesRemaining)])],
        Description: "A scroll that marks an area with a bright, temporary light.",
        Summary: "Marks the target area with a temporary torch light.",
        MaxStackSize: MaximumStackSize,
        Activator: new ScrollActivator(
            new TargetingSpec(Shape: TargetShape.Burst, Range: 5, AreaSize: 3),
            new ActionTiming(ActionTimingCategory.Immediate, (short)GameTiming.FramesForSeconds(1f), CooldownFrames: null),
            SpellId));
}
