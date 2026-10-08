using Engine.Math;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Tags;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory.Definitions;

public static class HealthPotion
{
    public static readonly Guid Id = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000001");

    public static ItemDefinition Build() => new(
        Id, "Health Potion", "HealthPotion", "h", Color.Green,
        Tags: [GameTags.EffectHealing, GameTags.TargetingSelf],
        Effects: [new Effect([new DirectHeal(0.5f)])],
        Description: "Increases your health by at least 50%. Doesn't cure poison or other health-seeping conditions such as succubus-inflicted gonorrhea. So remember to wrap it up, bucko.",
        Summary: "Heal target(s) by 50%.",
        GoldValue: 5,
        Activator: new PotionActivator(
            new TargetingSpec(Shape: TargetShape.Burst, Range: 3, AreaSize: 1, TargetModeAffects: TargetModeAffects.MarkedOnly),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null)));
}
