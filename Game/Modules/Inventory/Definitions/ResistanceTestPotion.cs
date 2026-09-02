using Engine.Math;
using Engine.Utilities;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Game.Modules.StatModifiers;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory.Definitions;

/// <summary>Test content for the status-effect effectiveness pillar (ConditionTag-scoped IncomingDamage) -- proves a real potion can grant a timed, damage-type-specific resistance end-to-end.</summary>
public static class ResistanceTestPotion
{
    public static readonly Guid Id = new("f1a4c7e2-3b9d-4e6f-8a2c-000000000031");

    private const int MaximumStackSize = 999;

    /// <summary>10 minutes -- fits ushort (36000 &lt; 65535).</summary>
    private const ushort DurationFrames = 10 * 60 * GameTiming.FramesPerSecond;

    private const float DamageReduction = -0.5f;

    public static ItemDefinition Build() => new(
        Id, "Draught of Insulation", "HealthPotion", "r", Color.Goldenrod,
        Tags: [Tag.Potion, Tag.Consumable, Tag.Self],
        Effects: [new ActionEffect([
            new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
                CanModify: false, Magnitude: DamageReduction, DurationFrames: DurationFrames, ConditionTag: Tag.Fire),
            new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
                CanModify: false, Magnitude: DamageReduction, DurationFrames: DurationFrames, ConditionTag: Tag.Poison),
        ])],
        Description: "A thick, insulating draught that dulls the sting of fire and venom alike, though only for a while.",
        Summary: "Reduces Burning and Poison damage taken by 50% for 10 minutes.",
        MaxStackSize: MaximumStackSize,
        GoldValue: 8,
        Activator: new PotionActivator(
            new TargetingSpec(Shape: TargetShape.Burst, Range: 3, AreaSize: 1),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null)));
}
