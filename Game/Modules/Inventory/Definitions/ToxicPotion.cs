using Engine.Math;
using Engine.Utilities;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory.Definitions;

public static class ToxicPotion
{
    public static readonly Guid Id = new("e72db9f1-9947-4b85-9cb4-0dcf38a8a53b");

    private const int MaximumStackSize = 999;

    public static ItemDefinition Build() => new(
        Id, "Toxic Flask", "HealthPotion", "x", Color.Purple,
        Tags: [Tag.Potion, Tag.Consumable],
        Effects: [new ActionEffect([
            new StatusEffectGrantEntry(StatusEffectType.Poison, StackCount: 5),
            new StatusEffectGrantEntry(StatusEffectType.Burning, StackCount: 3),
        ])],
        Description: "A sloshing flask of concentrated venom and embers. It doesn't hit hard on its own -- it hits long after you've stopped paying attention.",
        Summary: "Inflicts 5 stacks of Poison and 3 stacks of Burning.",
        MaxStackSize: MaximumStackSize,
        Activator: new PotionActivator(
            new TargetingSpec(Shape: TargetShape.Burst, Range: 3, AreaSize: 1),
            new ActionTiming(ActionTimingCategory.Immediate, (short)GameTiming.FramesForSeconds(1f), CooldownFrames: null)));
}
