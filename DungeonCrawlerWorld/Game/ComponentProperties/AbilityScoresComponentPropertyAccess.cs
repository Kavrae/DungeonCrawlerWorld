using Engine.ECS.Properties;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;

namespace Game.ComponentProperties;

/// <summary>AbilityScoresComponent's property table, by hand: a base and a total per AbilityScoreType, held in inline arrays and present only for a granted score.</summary>
/// <remarks>Rows are named by the enum. A score not granted reads as absent, and its base can't be written; a base outside AbilityScoreMath's range is refused. The total is what stat modifiers made of the base, so it is read-only and never saved.</remarks>
public sealed class AbilityScoresComponentPropertyAccess : ComponentPropertyAccess<AbilityScoresComponent>
{
    private static readonly ComponentPropertyMeaning ScoreType = new(GamePropertyMeanings.AbilityScoreType);

    private static readonly ComponentPropertyDescriptor BaseRow = new(0, "Base", ComponentPropertyKind.Integer, typeof(ushort), default, ComponentPropertyFlags.Optional);
    private static readonly ComponentPropertyDescriptor TotalRow = new(0, "Total", ComponentPropertyKind.Integer, typeof(ushort), default, ComponentPropertyFlags.Optional | ComponentPropertyFlags.ReadOnly | ComponentPropertyFlags.NotSaved | ComponentPropertyFlags.Derived);

    private static readonly ComponentPropertyDescriptor BaseProperty = new(0, "Base", ComponentPropertyKind.Indexed, typeof(ushort), ScoreType, ComponentPropertyFlags.None, element: BaseRow);
    private static readonly ComponentPropertyDescriptor TotalProperty = new(1, "Total", ComponentPropertyKind.Indexed, typeof(ushort), ScoreType, ComponentPropertyFlags.ReadOnly | ComponentPropertyFlags.NotSaved | ComponentPropertyFlags.Derived, element: TotalRow);

    public static readonly AbilityScoresComponentPropertyAccess Instance = new();

    public static ComponentPropertyTable Description { get; } = new(typeof(AbilityScoresComponent), [BaseProperty, TotalProperty]);

    public override ComponentPropertyTable Table => Description;

    public override void Read(in AbilityScoresComponent component, ComponentPropertyVisitor visitor)
    {
        visitor.BeginIndexed(BaseProperty, AbilityScoresComponent.Count);
        for (var type = 0; type < AbilityScoresComponent.Count; type++)
        {
            if (component.TryGet((AbilityScoreType)type, out var score))
            {
                visitor.Integer(BaseRow, score.BaseValue);
            }
            else
            {
                visitor.Absent(BaseRow);
            }
        }

        visitor.EndIndexed(BaseProperty);
        visitor.BeginIndexed(TotalProperty, AbilityScoresComponent.Count);
        for (var type = 0; type < AbilityScoresComponent.Count; type++)
        {
            if (component.TryGet((AbilityScoreType)type, out var score))
            {
                visitor.Integer(TotalRow, score.Total);
            }
            else
            {
                visitor.Absent(TotalRow);
            }
        }

        visitor.EndIndexed(TotalProperty);
    }

    public override bool Write(ref AbilityScoresComponent component, ReadOnlySpan<int> path, in ComponentPropertyValue value)
    {
        if (!IsBaseWithinRange(path, in value))
        {
            return false;
        }

        var type = (AbilityScoreType)path[1];
        if (!component.TryGet(type, out var score))
        {
            return false;
        }

        component.Set(type, (ushort)value.Integer, score.Total);
        return true;
    }

    /// <summary>Grants a score at its saved base. Its total is left at the base: the total is derived, never saved, and the restore's owner recomputes it from the entity's modifiers (AbilityScoreEffects.RecomputeAll).</summary>
    public override bool Restore(ref AbilityScoresComponent component, ReadOnlySpan<int> path, in ComponentPropertyValue value)
    {
        if (!IsBaseWithinRange(path, in value))
        {
            return false;
        }

        component.Set((AbilityScoreType)path[1], (ushort)value.Integer, (ushort)value.Integer);
        return true;
    }

    /// <summary>Whether path names one score's base and value is a base gameplay could hold: AbilityScoreMath's range, which every gameplay write clamps into.</summary>
    private static bool IsBaseWithinRange(ReadOnlySpan<int> path, in ComponentPropertyValue value) =>
        path.Length == 2 && path[0] == 0 && (uint)path[1] < AbilityScoresComponent.Count
            && !value.IsAbsent && value.Integer >= AbilityScoreMath.MinimumBaseValue && value.Integer <= AbilityScoreMath.MaximumBaseValue;
}
