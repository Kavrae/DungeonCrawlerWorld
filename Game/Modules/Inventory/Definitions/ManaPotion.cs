using Engine.Math;
using Engine.Utilities;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory.Definitions;

public static class ManaPotion
{
    public static readonly Guid Id = new("a4f2e8c1-6b3d-4e97-9a1f-2d8c5b7e4a63");

    private const int MaximumStackSize = 999;

    public static ItemDefinition Build() => new(
        Id, "Regular Mana Potion", "HealthPotion", "m", Color.Blue,
        Tags: [Tag.Potion, Tag.Consumable, Tag.Self],
        Effects: [new ActionEffect([new ManaRestoreEffectEntry(1f)])],
        Description: "Fully restores the target(s) mana. Oddly tastes like TV static.",
        Summary: "Restore target's mana.",
        MaxStackSize: MaximumStackSize,
        Activator: new PotionActivator(
            new TargetingSpec(Shape: TargetShape.Burst, Range: 3, AreaSize: 1),
            new ActionTiming(ActionTimingCategory.Immediate, (short)GameTiming.FramesForSeconds(1f), CooldownFrames: null)));
}
