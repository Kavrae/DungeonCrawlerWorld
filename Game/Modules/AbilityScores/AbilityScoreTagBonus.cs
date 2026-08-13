using Engine.ECS.Components.Stores;
using Game.Modules.AbilityScores.Components;

namespace Game.Modules.AbilityScores;

/// <summary>
/// Sums the caster's own Total for every ability score whose matching Tag the given tag list
/// carries -- generic by design, not specific to any one activator: an ability tagged
/// Tag.Strength and a future damaging consumable tagged the same way both get the identical
/// bonus through this one path. Relocated from AbilityEffectResolver's private
/// ComputeAbilityScoreBonus/MapTagToAbilityScore, now usable by any DirectDamage regardless
/// of which activator kind carries it. No-op (returns 0) when abilityScores is null
/// (AbilityScoresModule not registered in this build).
/// </summary>
public static class AbilityScoreTagBonus
{
    public static short Compute(int sourceEntityId, IReadOnlyList<Tag> tags, MultiComponentPool<AbilityScoreComponent>? abilityScores)
    {
        if (abilityScores is null)
        {
            return 0;
        }

        short bonus = 0;
        foreach (var tag in tags)
        {
            if (MapTagToAbilityScore(tag) is { } scoreType &&
                AbilityScoreQueries.TryGetComponent(abilityScores, sourceEntityId, scoreType, out var score))
            {
                bonus += score.Total;
            }
        }

        return bonus;
    }

    private static AbilityScoreType? MapTagToAbilityScore(Tag tag) => tag switch
    {
        Tag.Strength => AbilityScoreType.Strength,
        Tag.Intelligence => AbilityScoreType.Intelligence,
        Tag.Constitution => AbilityScoreType.Constitution,
        Tag.Dexterity => AbilityScoreType.Dexterity,
        Tag.Charisma => AbilityScoreType.Charisma,
        Tag.Luck => AbilityScoreType.Luck,
        Tag.Wisdom => AbilityScoreType.Wisdom,
        _ => null,
    };
}
