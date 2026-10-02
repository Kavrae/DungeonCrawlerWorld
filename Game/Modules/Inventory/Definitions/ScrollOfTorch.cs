using Engine.Math;
using Game.Effects;
using Engine.Utilities;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Auras;
using Game.Tags;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory.Definitions;

/// <summary>
/// Second concrete ScrollActivator item -- proves ScrollScalingEffects (Range/AreaSize/duration
/// all scale together off the caster's Intelligence, 100% at 1 up to 400% at 300) and the
/// mastery-*synthesis* half of ScrollMasteryEffects: SpellId is a brand-new Guid with no existing
/// ActionDefinition, so mastering this scroll builds and registers a fresh "Torch" spell at
/// runtime instead of looking one up.
///
/// Effect is AuraSourceGrant's timed mode granting the scroll's own light aura (Aura) -- a purely
/// cosmetic map glow today (MapWindow draws it generically from AuraGlowView, the same pipeline
/// Lava's own Burning glow already uses, with zero Torch-specific knowledge anywhere in
/// Presentation). AuraStrength: 8 matches this scroll's own base AreaSize (3) via
/// DistanceFalloff.MaxRadius(strength) = log2(strength), the same way Lava's Strength 8 produces
/// a 3-tile range -- fixed, not itself re-derived from the scaled targeting AreaSize (only
/// Duration is explicitly scaled here, same as every other scroll effect entry). Future TODO:
/// reveal fog of war in its AOE and damage entities with a light weakness (vampires) -- give
/// Aura effects once that lands.
/// </summary>
public static class ScrollOfTorch
{
    public static readonly Guid Id = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000021");
    public static readonly Guid SpellId = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000022");

    private const int BaseFramesRemaining = GameTiming.FramesPerSecond * 10; // 10s at Intelligence 1 (100%)
    private const byte AuraStrength = 8; // -> 3-tile reach, matching this scroll's own base AreaSize

    /// <summary>The torch's own aura: light, which only glows.</summary>
    public static readonly AuraDefinition Aura = new(new Guid("d9f6a1c4-8b2e-4f3a-9c1d-000000000303"), "Light", Color.White);

    public static ItemDefinition Build() => new(
        Id, "Scroll of Torch", "Scroll", "t", Color.White,
        Tags: [GameTags.TargetingSelf],
        Effects: [new Effect([new AuraSourceGrant(Aura, AuraStrength, DurationFrames: BaseFramesRemaining)])],
        Description: "A scroll that marks an area with a bright, temporary light.",
        Summary: "Marks the target area with a temporary torch light.",
        GoldValue: 14,
        Activator: new ScrollActivator(
            new TargetingSpec(Shape: TargetShape.Burst, Range: 5, AreaSize: 3),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null),
            SpellId));
}
