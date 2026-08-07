using Engine.ECS.Components;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Core.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Presentation.UI.AbilityScores;

/// <summary>
/// Builds the display lines for one ability-score column's scrolling list: "Base : N" always
/// first, then one line per active StatModifierComponent targeting that score, flat (Additive)
/// before multiplicative and positive before negative within each -- per the window's own spec.
/// Pure logic, no rendering -- assumes the caller (AbilityScoreWindow) only calls this for an
/// entity that actually has AbilityScoreComponent/StatModifierComponent pools registered, the
/// same trust-the-caller boundary InventoryGridContent already draws for its own component reads.
/// </summary>
public static class AbilityScoreModifierFormatter
{
    public static IReadOnlyList<string> GetOrderedLines(ComponentManager componentManager, int entityId, AbilityScoreType type)
    {
        var lines = new List<string> { $"Base : {GetBaseValue(componentManager, entityId, type)}" };

        if (!componentManager.IsRegistered<StatModifierComponent>())
        {
            return lines;
        }

        var target = AbilityScoreMath.ToStatModifierTarget(type);
        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();

        var modifiers = new List<StatModifierComponent>();
        for (var denseIndex = statModifiers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = statModifiers.GetNextDenseIndex(denseIndex))
        {
            var modifier = statModifiers.GetReadonlyByDenseIndex(denseIndex);
            if (modifier.Target == target)
            {
                modifiers.Add(modifier);
            }
        }

        // OrderBy/ThenBy are stable, unlike List<T>.Sort -- ties (same Operation, same sign)
        // keep the dense-chain order they were found in, since no further ordering was specified.
        var ordered = modifiers
            .OrderBy(static modifier => modifier.Operation == StatModifierOperation.Multiplicative)
            .ThenBy(static modifier => modifier.Magnitude < 0);

        foreach (var modifier in ordered)
        {
            lines.Add(FormatModifierLine(componentManager, modifier));
        }

        return lines;
    }

    private static short GetBaseValue(ComponentManager componentManager, int entityId, AbilityScoreType type) =>
        AbilityScoreQueries.TryGetComponent(componentManager.GetMultiPool<AbilityScoreComponent>(), entityId, type, out var component)
            ? component.BaseValue
            : throw new InvalidOperationException($"No AbilityScoreComponent of type {type} for entity {entityId}.");

    private static string FormatModifierLine(ComponentManager componentManager, StatModifierComponent modifier)
    {
        var sourceName = DescribeSource(componentManager, modifier.Source);
        return modifier.Operation == StatModifierOperation.Additive
            ? $"{sourceName} : {FormatSigned((int)MathF.Round(modifier.Magnitude))}"
            : $"{sourceName} : {FormatSigned((int)MathF.Round(modifier.Magnitude * 100))}%";
    }

    private static string FormatSigned(int value) => value >= 0 ? $"+{value}" : value.ToString();

    /// <summary>Admin/AI have no entity to name (StatusEffectSource.ToString() already covers them); an Entity source resolves DisplayTextComponent.Name if present, else falls back to a numeric label -- same fallback shape as Game/Diagnostics/PlayerActivityLog.cs's DescribeSource/DescribeEntity, replicated here rather than shared since that's a private method on an unrelated diagnostics class.</summary>
    private static string DescribeSource(ComponentManager componentManager, StatusEffectSource source)
    {
        if (source.Kind != StatusEffectSourceKind.Entity)
        {
            return source.ToString();
        }

        return componentManager.IsRegistered<DisplayTextComponent>()
            && componentManager.GetDirectPool<DisplayTextComponent>().TryGetReadonly(source.EntityId, out var displayText)
            ? displayText.Name
            : $"Entity#{source.EntityId}";
    }
}
