using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;

namespace Game.Views;

/// <summary>An entity's ability scores.</summary>
public sealed class AbilityScoreView(ComponentManager componentManager)
{
    private readonly PackedComponentPool<AbilityScoresComponent> _abilityScores = componentManager.GetPackedPool<AbilityScoresComponent>();

    /// <summary>entityId's score of type, if it has ability scores.</summary>
    public bool TryGetAbilityScore(int entityId, AbilityScoreType type, out AbilityScoreValue abilityScore) =>
        AbilityScoreQueries.TryGetComponent(_abilityScores, entityId, type, out abilityScore);

    /// <summary>Changes whenever entityId's ability scores are written.</summary>
    public uint GetVersion(int entityId) => _abilityScores.GetVersion(entityId);
}
