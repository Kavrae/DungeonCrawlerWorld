using Engine.Math;
using Engine.Utilities;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory.Definitions;

/// <summary>Test content for the status-effect prevention pillar (StatusEffectImmunityGrant) -- proves a real potion can grant timed immunity end-to-end.</summary>
public static class ImmunityTestPotion
{
    public static readonly Guid Id = new("f1a4c7e2-3b9d-4e6f-8a2c-000000000030");

    /// <summary>10 minutes -- fits ushort (36000 &lt; 65535).</summary>
    private const ushort DurationFrames = 10 * 60 * GameTiming.FramesPerSecond;

    public static ItemDefinition Build() => new(
        Id, "Vial of Warding", "HealthPotion", "w", Color.Cyan,
        Tags: [Tag.Potion, Tag.Consumable, Tag.Self],
        Effects: [new ActionEffect([
            new StatusEffectImmunityGrant(StatusEffectType.Burning, DurationFrames),
            new StatusEffectImmunityGrant(StatusEffectType.Poison, DurationFrames),
        ])],
        Description: "A shimmering vial that coats the drinker in a ward against fire and venom alike. The protection is complete, but not permanent.",
        Summary: "Grants immunity to Burning and Poison for 10 minutes.",
        GoldValue: 3,
        Activator: new PotionActivator(
            new TargetingSpec(Shape: TargetShape.Burst, Range: 3, AreaSize: 1),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null)));
}
