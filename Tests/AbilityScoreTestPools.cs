using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;

namespace Tests;

/// <summary>Builds the ability score pool and single-score components the way AbilityScoresModule does, for tests that wire a minimal module set by hand.</summary>
internal static class AbilityScoreTestPools
{
    internal static PackedComponentPool<AbilityScoresComponent> CreatePool(int entityCapacity, int initialCapacity) =>
        new(entityCapacity, initialCapacity, Merge);

    internal static AbilityScoresComponent Score(AbilityScoreType type, ushort baseValue, ushort total)
    {
        var scores = default(AbilityScoresComponent);
        scores.Set(type, baseValue, total);
        return scores;
    }

    private static void Merge(ref AbilityScoresComponent existing, AbilityScoresComponent incoming) => existing.MergeFrom(incoming);
}
