using Engine.Tags;
using Game.Effects;
using Game.Modules.Auras;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Effects.Entries;
using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory.Definitions;

/// <summary>A toggle item: while a unit is lit, whoever holds it radiates a Poison aura.</summary>
/// <remarks>
/// Never consumed. The lit state is the unit's own and its aura belongs to whoever holds it, so a lit
/// idol given away, sold or left on a corpse keeps radiating from there, and each lit unit adds its
/// own source.
/// </remarks>
public static class ToxicIdol
{
    public static readonly Guid Id = new("f3a8c1d6-2b4e-4a9f-8c6d-1e7b3a5f9c2d");

    private const ushort AuraPower = 16;

    private const byte AuraSize = 4;

    /// <summary>The idol's own aura: each tick tops Poison stacks up to the aura's power at the entity.</summary>
    public static readonly AuraDefinition Aura = new(new Guid("d9f6a1c4-8b2e-4f3a-9c1d-000000000302"), "Poison", Color.DarkGreen,
        [new Effect([new StatusEffectGrant(StatusEffectType.Poison, StackCount: 1, StatusEffectGrantMode.TopUpTo)])]);

    public static ItemDefinition Build() => new(
        Id, "Toxic Idol", "HealthPotion", "i", Color.DarkGreen,
        Tags: GameplayTagSet.Empty,
        Effects: [new Effect([new AuraSourceGrant(Aura, AuraPower, AuraSize)])],
        Description: "A squat stone idol weeping a slow green ichor. While it is lit, whoever holds it is wreathed in a spreading toxic cloud -- useful for softening a crowd, less so for standing still in one.",
        Summary: "Toggles a Poison aura (range 4) around whoever holds it.",
        GoldValue: 15,
        Activator: new ToggleItemActivator(new ActionTiming(ActionTimingCategory.Delayed)),
        Toggle: ToggleSpec.HoldsEffectsOnly);
}
