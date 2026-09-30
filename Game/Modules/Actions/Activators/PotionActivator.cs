using Engine.Math;
using Engine.Tags;
using Game.Tags;

namespace Game.Modules.Actions.Activators;

/// <summary>A potion activator that applies its effects when used.</summary>
/// <remarks>Consumes stacks of potions when used.
/// Activates a potion cooldown that scales with Constitution. Having a potion activated
/// on an entity during this cooldownalso adds stacks of poison to that same entity.</remarks>
/// <param name="Targeting">The targeting specification for the potion.</param>
/// <param name="Timing">The timing specification for the potion.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record PotionActivator(TargetingSpec Targeting, ActionTiming Timing) : IActionActivator
{
    private static readonly GameplayTagSet PotionImpliedTags = [GameTags.ItemConsumablePotion];

    public GameplayTagSet ImpliedTags => PotionImpliedTags;
}
