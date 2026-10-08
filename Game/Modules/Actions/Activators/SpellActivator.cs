using Engine.Math;
using Engine.Tags;
using Game.Tags;

namespace Game.Modules.Actions.Activators;

/// <summary>Action activator for casting spells.</summary>
/// <remarks>A spell's mana cost is a ManaDrain among its definition's ActivationEffects, like any other cost.</remarks>
/// <param name="Targeting">The targeting specification for the spell.</param>
/// <param name="Timing">The timing specification for the spell.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record SpellActivator(TargetingSpec Targeting, ActionTiming Timing) : IActionActivator
{
    private static readonly GameplayTagSet SpellImpliedTags = [GameTags.ActionSpell, GameTags.Magic];

    public GameplayTagSet ImpliedTags => SpellImpliedTags;
}
