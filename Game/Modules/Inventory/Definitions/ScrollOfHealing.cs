using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Definitions.Spells;
using Game.Modules.Actions.Effects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory.Definitions;

/// <summary>
/// Heal the target entity by 50% of their maximum HP
/// </summary>
/// <remarks>
/// Standard single target burst healing.
/// </remarks>
public static class ScrollOfHealing
{
    public static readonly Guid Id = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000020");

    private const int MaximumStackSize = 999;

    private const float HealAmount = 0.5f;

    public static ItemDefinition Build() => new(
        Id, "Scroll of Healing", "Scroll", "s", Color.White,
        Tags: [Tag.Scroll, Tag.Consumable, Tag.Healing, Tag.Self],
        Effects: [new ActionEffect([new DirectHeal(HealAmount)])],
        Description: "A scroll inscribed with the Heal spell. Heals the target by a percentage of their maximum health and then crumbles to dust.",
        Summary: $"Heal target(s) by {HealAmount:P0}.",
        MaxStackSize: MaximumStackSize,
        Activator: new ScrollActivator(
            new TargetingSpec(Shape: TargetShape.AdjacentWithSelf, Range: 0),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null),
            SpellId: HealAction.Id));
}
