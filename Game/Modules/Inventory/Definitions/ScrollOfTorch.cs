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
/// Presentation). The grant is placed once per activation (AuraSourceGrant): read at someone in Target
/// mode the light follows them; read at the ground, or at an empty tile, it is anchored there
/// (AuraAnchors). AuraSize 3 is fixed; only Range and Duration scale. Future TODO: reveal fog of
/// war in its AOE and damage entities with a light weakness (vampires) -- give Aura effects once
/// that lands.
/// </summary>
public static class ScrollOfTorch
{
    public static readonly Guid Id = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000021");
    public static readonly Guid SpellId = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000022");

    private const int BaseFramesRemaining = GameTiming.FramesPerSecond * 10; // 10s at Intelligence 1 (100%)
    private const ushort AuraPower = 8;
    private const byte AuraSize = 3;

    /// <summary>The torch's own aura: light, which only glows.</summary>
    public static readonly AuraDefinition Aura = new(new Guid("d9f6a1c4-8b2e-4f3a-9c1d-000000000303"), "Light", Color.White);

    public static ItemDefinition Build() => new(
        Id, "Scroll of Torch", "Scroll", "t", Color.White,
        Tags: [GameTags.TargetingSelf],
        Effects: [new Effect([new AuraSourceGrant(Aura, AuraPower, AuraSize, DurationFrames: BaseFramesRemaining)])],
        Description: "A scroll that sets a bright, temporary light on whoever it is read at -- or, read at the ground, on that spot.",
        Summary: "Sets a temporary light on the target, or on the spot aimed at.",
        GoldValue: 14,
        Activator: new ScrollActivator(
            new TargetingSpec(Shape: TargetShape.SingleTarget, Range: 5),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null),
            SpellId));
}
