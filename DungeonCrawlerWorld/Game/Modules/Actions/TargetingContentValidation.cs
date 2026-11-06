using Engine.Tiles;
using Engine.Tags;
using Game.Tags;

namespace Game.Modules.Actions;

/// <summary>Checks that every activation the build's registered content defines has targeting that means what it says: Target mode only where it makes sense, and no setting that would do nothing.</summary>
/// <remarks>
/// A melee swing (Delivery.Melee) and an Adjacent shape anchored on the caster strike the tiles around the caster, with no
/// tile aimed at to mark anyone on, so either must be TargetingModes.GroundOnly. A minimum range past the aim's maximum
/// (TargetingSpec.AimRanges) fails, and so does any setting the targeting never reads: a maximum range on a Placed shape
/// anchored on its caster (aimed within its own area), Self away from the caster or SingleTarget on it, an area size on
/// shapes with no size, a Lobbed path that doesn't land on its aim, hit counts and path ends without a Projectile, wall
/// rules without a path, and bounces without BouncesOffWalls. ProblemsOf is the same rules for one definition, for one made at
/// runtime. Run beside ToggleContentValidation, so a mod breaking it fails
/// its dry run.
/// </remarks>
internal static class TargetingContentValidation
{
    /// <exception cref="InvalidOperationException">A definition offers Target mode it can't have, naming the definition and why.</exception>
    public static void EnsureValid(GameModuleContext context)
    {
        foreach (var action in context.Actions.Definitions)
        {
            ThrowFirst(ProblemsOf(action));
        }

        foreach (var item in context.Items.Definitions)
        {
            ThrowFirst(ProblemsOf(item));
        }
    }

    /// <summary>Every targeting rule action breaks, as a sentence naming it.</summary>
    public static IReadOnlyList<string> ProblemsOf(ActionDefinition action) => ProblemsOf(action.Activator.Targeting, action.Tags, $"Action '{action.Name}'");

    /// <summary>Every targeting rule item breaks, as a sentence naming it; none for an item with no activator.</summary>
    public static IReadOnlyList<string> ProblemsOf(Inventory.ItemDefinition item) =>
        item.Activator is { } activator ? ProblemsOf(activator.Targeting, item.Tags, $"Item '{item.Name}'") : [];

    /// <summary>The shapes AreaSize sizes.</summary>
    private const TargetShape SizedShapes = TargetShape.Line | TargetShape.Cone | TargetShape.Circle | TargetShape.Ring | TargetShape.Wall | TargetShape.Square | TargetShape.Cross;

    private static List<string> ProblemsOf(TargetingSpec targeting, GameplayTagSet tags, string definedBy)
    {
        var problems = new List<string>();
        AddUnreadSettings(targeting, definedBy, problems);

        if (targeting.MinimumRange > targeting.AimRanges.MaximumRange)
        {
            problems.Add($"{definedBy} has a minimum range ({targeting.MinimumRange}) past the farthest it can be aimed ({targeting.AimRanges.MaximumRange}): nothing could be aimed at.");
        }

        if (targeting.Modes == TargetingModes.GroundOnly)
        {
            return problems;
        }

        if (tags.Has(GameTags.DeliveryMelee))
        {
            problems.Add($"{definedBy} is Delivery.Melee but offers Target mode: a melee activation must be TargetingModes.GroundOnly.");
        }

        if ((targeting.Shape & TargetShape.Adjacent) != 0 && targeting.Anchor == TargetAnchor.Caster)
        {
            problems.Add($"{definedBy} has an Adjacent shape anchored on its caster but offers Target mode: it strikes the tiles around its caster, so it must be TargetingModes.GroundOnly.");
        }

        return problems;
    }

    /// <summary>Adds each setting targeting holds that it never reads, naming the definition and the setting.</summary>
    private static void AddUnreadSettings(TargetingSpec targeting, string definedBy, List<string> problems)
    {
        if (targeting.Anchor == TargetAnchor.Caster && targeting.Path == TargetPath.Placed && targeting.MaximumRange != 0)
        {
            problems.Add($"{definedBy} has a maximum range ({targeting.MaximumRange}) but is placed around its caster, where it is aimed within its own area size: leave the maximum range out.");
        }

        if (targeting.Anchor == TargetAnchor.PathEnd && (targeting.Shape & TargetShape.Self) != 0)
        {
            problems.Add($"{definedBy} has a Self shape where its path ends: Self is the caster's own tiles, at a Caster anchor; the end tile is SingleTarget.");
        }

        if (targeting.Anchor == TargetAnchor.Caster && (targeting.Shape & TargetShape.SingleTarget) != 0)
        {
            problems.Add($"{definedBy} has a SingleTarget shape anchored on its caster: SingleTarget is the end tile, at a PathEnd anchor; the caster's own tiles are Self.");
        }

        if (targeting.AreaSize != 0 && (targeting.Shape & SizedShapes) == 0)
        {
            problems.Add($"{definedBy} has an area size ({targeting.AreaSize}) but its shape ({targeting.Shape}) has no size: leave the area size out.");
        }

        if (targeting.Path == TargetPath.Lobbed && targeting.PathEnd != ProjectilePathEnd.AtAimedTile)
        {
            problems.Add($"{definedBy} has a Lobbed path ending {targeting.PathEnd}: a Lobbed path always lands on its aimed tile, so leave the path end out.");
        }

        if (targeting.Path != TargetPath.Projectile && (targeting.ProjectileHitCount != 0 || targeting.PathEnd != ProjectilePathEnd.AtAimedTile))
        {
            problems.Add($"{definedBy} has a projectile hit count or path end but its path is {targeting.Path}: only a Projectile reads them.");
        }

        if (targeting.Path == TargetPath.Placed && targeting.PathWalls != PathWallRule.StopsAtWall)
        {
            problems.Add($"{definedBy} has a path wall rule ({targeting.PathWalls}) but no path: leave it out.");
        }

        if (targeting.WallBounceCount != 0 && (targeting.Path == TargetPath.Placed || targeting.PathWalls != PathWallRule.BouncesOffWalls))
        {
            problems.Add($"{definedBy} has a wall bounce count ({targeting.WallBounceCount}) but its path doesn't bounce off walls: leave it out.");
        }
    }

    private static void ThrowFirst(IReadOnlyList<string> problems)
    {
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(problems[0]);
        }
    }
}
