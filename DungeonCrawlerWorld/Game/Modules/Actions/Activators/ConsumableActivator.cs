using Engine.Tags;
using Engine.Tiles;
using Game.Tags;

namespace Game.Modules.Actions.Activators;

/// <summary>Item-triggered activator for a one-use item with no rule of its own: one unit is used up and its effects land on whatever the targeting reaches.</summary>
/// <remarks>
/// What the item does is all in its Effects (a tome is a consumable holding a SpellGrant). Potions, scrolls and wands
/// keep activators of their own because each adds a rule this one doesn't: the potion cooldown, scroll mastery and
/// scaling, a wand's charges. A consumable whose targeting reaches only its user is refused rather than used up when
/// its effects can't apply to that user (ActivationQueries.GetBlocker).
/// </remarks>
/// <param name="Targeting">The targeting specification for the item.</param>
/// <param name="Timing">The timing specification for the item.</param>
public sealed record ConsumableActivator(TargetingSpec Targeting, ActionTiming Timing) : IActionActivator
{
    private static readonly GameplayTagSet ConsumableImpliedTags = [GameTags.ItemConsumable];

    public GameplayTagSet ImpliedTags => ConsumableImpliedTags;

    /// <summary>Whether the targeting reaches only the user, so the effects can be asked of the user before a unit is used up.</summary>
    public bool ReachesOnlyUser => Targeting.Shape == TargetShape.Self;
}
