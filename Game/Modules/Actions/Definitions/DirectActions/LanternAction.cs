using Engine.Math;
using Engine.Utilities;
using Game.Effects;
using Game.Modules.Actions.Activators;
using Game.Modules.Auras;
using Game.Tags;
using Microsoft.Xna.Framework;

namespace Game.Modules.Actions.Definitions.DirectActions;

/// <summary>TEMPORARY test content: a toggle that holds a glow-only light, to see a toggle place an aura anchor.</summary>
/// <remarks>
/// Switched on in Target mode the light is on whoever has it on and follows them; in Ground mode it is
/// anchored on the tile they stand on, stays there when they walk away, and goes when the toggle is
/// switched off -- or when its neighborhood unloads, which switches the toggle off. Remove once real
/// content places an anchor from a toggle.
/// </remarks>
public static class LanternAction
{
    public static readonly Guid Id = new("8e4b2c71-5d3a-4f96-b1e8-2a7c9d4f6b13");

    private const ushort AuraPower = 8;

    private const byte AuraSize = 4;

    private static readonly ushort OneSecondFrames = GameTiming.FramesForSeconds(1f);

    /// <summary>The lantern's own aura: a warm light, which only glows.</summary>
    public static readonly AuraDefinition Aura = new(new Guid("d9f6a1c4-8b2e-4f3a-9c1d-000000000309"), "Lantern", Color.LightGoldenrodYellow);

    public static ActionDefinition Build() => new(
        Id, "Lantern", null, "l", Color.LightGoldenrodYellow,
        Tags: [GameTags.TargetingSelf],
        Effects: [new Effect([new AuraSourceGrant(Aura, AuraPower, AuraSize)])],
        Activator: new DirectAction(
            new TargetingSpec(TargetShape.Self, Range: 0),
            new ActionTiming(ActionTimingCategory.FreeCast, CooldownFrames: OneSecondFrames)),
        Description: "A lantern that lights the way -- carried, or set down where you stand.",
        Summary: "Toggles a light: carried in Target mode, set down in Ground mode.",
        Toggle: ToggleSpec.HoldsEffectsOnly);
}
