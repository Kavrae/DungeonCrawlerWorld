using Engine.ECS.Components.Stores;
using Engine.Tags;
using Game.Modules.AbilityScores.Components;
using Game.Tags;

namespace Game.Modules.AbilityScores;

/// <summary>
/// Sums the caster's own Total for every ability score whose Stats.AbilityScore tag the given tag list
/// carries -- generic by design, not specific to any one activator: an ability tagged
/// GameTags.StatsAbilityScoreStrength and a future damaging consumable tagged the same way both get the
/// identical bonus through this one path. Relocated from AbilityEffectResolver's private
/// ComputeAbilityScoreBonus/MapTagToAbilityScore, now usable by any DirectDamage regardless
/// of which activator kind carries it.
/// </summary>
public static class AbilityScoreTagBonus
{
    private static readonly (GameplayTag Tag, AbilityScoreType ScoreType)[] AbilityScoreTagScores =
    [
        (GameTags.StatsAbilityScoreStrength, AbilityScoreType.Strength),
        (GameTags.StatsAbilityScoreIntelligence, AbilityScoreType.Intelligence),
        (GameTags.StatsAbilityScoreConstitution, AbilityScoreType.Constitution),
        (GameTags.StatsAbilityScoreDexterity, AbilityScoreType.Dexterity),
        (GameTags.StatsAbilityScoreCharisma, AbilityScoreType.Charisma),
        (GameTags.StatsAbilityScoreLuck, AbilityScoreType.Luck),
        (GameTags.StatsAbilityScoreWisdom, AbilityScoreType.Wisdom),
    ];

    public static ushort Compute(int sourceEntityId, GameplayTagSet tags, PackedComponentPool<AbilityScoresComponent> abilityScores)
    {
        ushort bonus = 0;
        foreach (var tag in tags)
        {
            if (TryMapTagToAbilityScore(tag, out var scoreType) &&
                AbilityScoreQueries.TryGetComponent(abilityScores, sourceEntityId, scoreType, out var score))
            {
                bonus += score.Total;
            }
        }

        return bonus;
    }

    private static bool TryMapTagToAbilityScore(GameplayTag tag, out AbilityScoreType scoreType)
    {
        if (tag.IsSelfOrDescendantOf(GameTags.StatsAbilityScore))
        {
            foreach (var (abilityScoreTag, abilityScoreType) in AbilityScoreTagScores)
            {
                if (tag == abilityScoreTag)
                {
                    scoreType = abilityScoreType;
                    return true;
                }
            }
        }

        scoreType = default;
        return false;
    }
}
