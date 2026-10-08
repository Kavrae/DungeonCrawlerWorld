using Engine.Math;
using Engine.Tags;
using Game.Tags;

namespace Game.Modules.Actions.Activators;

/// <summary>Item-triggered activator for a toggle item: never consumed, and each unit is either lit or not.</summary>
/// <remarks>
/// What the item holds, takes and does as a toggle is its definition's ToggleSpec; this says only how
/// its units are spent (they aren't) and how it is triggered (Timing). IsToggledOn is one unit's
/// state, so it lives in the stack's Override and travels with the item through every transfer: a
/// lit unit is a stack whose Override carries this activator with IsToggledOn true. It changes only
/// by moving a unit between the lit and unlit stacks (ToggleItemActions), never by rewriting a stack
/// in place. A toggle applies to its holder, so Targeting is always Self.
/// </remarks>
/// <param name="Timing">How turning the item on or off is timed: FreeCast ignores the action lock, Immediate waits for it and sets it.</param>
/// <param name="IsToggledOn">Whether this unit is lit.</param>
public sealed record ToggleItemActivator(ActionTiming Timing, bool IsToggledOn = false) : IActionActivator
{
    private static readonly TargetingSpec SelfTargeting = new(Shape: TargetShape.Self, Range: 0, AreaSize: 0);

    private static readonly GameplayTagSet ToggleImpliedTags = [GameTags.ItemToggle];

    public TargetingSpec Targeting => SelfTargeting;

    public GameplayTagSet ImpliedTags => ToggleImpliedTags;
}
