using Engine.Math;
using Engine.Tags;
using Game.Tags;

namespace Game.Modules.Actions;

/// <summary>Checks that every activation the build's registered content defines offers Target mode only where it makes sense.</summary>
/// <remarks>
/// A melee swing (Delivery.Melee) and an Adjacent shape strike the tiles around the caster, with no
/// tile aimed at to mark anyone on, so either must be TargetingModes.GroundOnly. Run beside
/// ToggleContentValidation, so a mod breaking it fails its dry run.
/// </remarks>
internal static class TargetingContentValidation
{
    /// <exception cref="InvalidOperationException">A definition offers Target mode it can't have, naming the definition and why.</exception>
    public static void EnsureValid(GameModuleContext context)
    {
        foreach (var action in context.Actions.Definitions)
        {
            EnsureValid(action.Activator.Targeting, action.Tags, $"Action '{action.Name}'");
        }

        foreach (var item in context.Items.Definitions)
        {
            if (item.Activator is { } activator)
            {
                EnsureValid(activator.Targeting, item.Tags, $"Item '{item.Name}'");
            }
        }
    }

    private static void EnsureValid(TargetingSpec targeting, GameplayTagSet tags, string definedBy)
    {
        if (targeting.Modes == TargetingModes.GroundOnly)
        {
            return;
        }

        if (tags.Has(GameTags.DeliveryMelee))
        {
            throw new InvalidOperationException($"{definedBy} is Delivery.Melee but offers Target mode: a melee activation must be TargetingModes.GroundOnly.");
        }

        if ((targeting.Shape & TargetShape.Adjacent) != 0)
        {
            throw new InvalidOperationException($"{definedBy} has an Adjacent shape but offers Target mode: it strikes the tiles around its caster, so it must be TargetingModes.GroundOnly.");
        }
    }
}
