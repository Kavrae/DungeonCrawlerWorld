using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory.Definitions;

public static class DamagePotion
{
    public static readonly Guid Id = new("136d0466-3749-40c4-aea4-5f02254040b3");

    private const int MaximumStackSize = 999;

    public static ItemDefinition Build() => new(
        Id, "Volatile Concoction", "HealthPotion", "d", Color.OrangeRed,
        Tags: [Tag.Potion, Tag.Consumable],
        Effects: [new ActionEffect([new DirectDamage(MinAmount: 20, MaxAmount: 30)])],
        Description: "A viscerally unstable brew that bursts into caustic shrapnel on impact. Whatever it's made of, it was never meant to be swallowed -- throw it instead.",
        Summary: "Deals damage to target(s).",
        MaxStackSize: MaximumStackSize,
        Activator: new PotionActivator(
            new TargetingSpec(Shape: TargetShape.Burst, Range: 3, AreaSize: 1),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null)));
}
