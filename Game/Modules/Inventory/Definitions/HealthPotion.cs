using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory.Definitions;

public static class HealthPotion
{
    public static readonly Guid Id = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000001");

    private const int MaximumStackSize = 999;

    public static ItemDefinition Build() => new(
        Id, "Health Potion", "HealthPotion", "h", Color.Green,
        Tags: [Tag.Potion, Tag.Consumable, Tag.Healing, Tag.Self],
        Effects: [new ActionEffect([new DirectHeal(0.5f)])],
        Description: "Increases your health by at least 50%. Doesn't cure poison or other health-seeping conditions such as succubus-inflicted gonorrhea. So remember to wrap it up, bucko.",
        Summary: "Heal target(s) by 50%.",
        MaxStackSize: MaximumStackSize,
        GoldValue: 5,
        Activator: new PotionActivator(
            new TargetingSpec(Shape: TargetShape.Burst, Range: 3, AreaSize: 1),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null)));
}
