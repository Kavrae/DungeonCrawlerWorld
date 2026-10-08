using Engine.Math;
using Engine.Utilities;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.StatusEffects;
using Game.Modules.Actions.Activators;
using Game.Modules.Auras;
using Game.Tags;
using Microsoft.Xna.Framework;

namespace Game.Modules.Actions.Definitions.DirectActions;

/// <summary>A toggle action: while it is on, the entity radiates a Poison aura -- or sets it down where it stands, in Ground mode -- is immune to Poison, and pays 1 mana a second for it.</summary>
/// <remarks>
/// FreeCast, so it is switched on and off whatever the action lock, gated only by its own one-second
/// cooldown, which starts both ways. Turning it on takes nothing; the first mana is drained one
/// second later, and the aura switches itself off at the first second it can't be paid for. Its aura
/// is its own definition, so it and a Toxic Idol are two auras that each top Poison up, not one aura
/// whose powers add. It also holds a Poison immunity on its user while it is on: a cloud set down in
/// Ground mode reaches whoever set it down, like any aura on a tile, and this one must not poison its user.
/// </remarks>
public static class ToxicAuraAction
{
    public static readonly Guid Id = new("5c2e8a47-1d9b-4f36-a8c1-7e3b9d5f2a64");

    private const ushort AuraPower = 16;

    private const byte AuraSize = 4;

    private const ushort ManaPerSecond = 1;

    private static readonly ushort OneSecondFrames = GameTiming.FramesForSeconds(1f);

    /// <summary>The action's own aura: each tick tops Poison stacks up to the aura's power at the entity.</summary>
    public static readonly AuraDefinition Aura = new(new Guid("d9f6a1c4-8b2e-4f3a-9c1d-000000000306"), "Toxic Aura", Color.DarkGreen,
        [new Effect([new StatusEffectGrant(StatusEffectType.Poison, StackCount: 1, StatusEffectGrantMode.TopUpTo)])]);

    public static ActionDefinition Build() => new(
        Id, "Toxic Aura", "HealthPotion", "a", Color.DarkGreen,
        Tags: [GameTags.TargetingSelf],
        Effects: [new Effect([new StatusEffectImmunityGrant(StatusEffectType.Poison), new AuraSourceGrant(Aura, AuraPower, AuraSize)])],
        Activator: new DirectAction(
            new TargetingSpec(TargetShape.Self, Range: 0),
            new ActionTiming(ActionTimingCategory.FreeCast, CooldownFrames: OneSecondFrames)),
        Description: "Wreathe yourself in a spreading toxic cloud for as long as you can sustain it. The cloud feeds on your mana and fades the moment there is none left to give.",
        Summary: "Toggles a Poison aura (range 4) around you. Drains 1 mana a second.",
        Toggle: new ToggleSpec(
            Periodic: new TogglePeriodicEffects([new Effect([new ManaDrain(ManaPerSecond)])], OneSecondFrames)));
}
