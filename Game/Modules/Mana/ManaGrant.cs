using Engine.ECS.Components;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Mana.Components;

namespace Game.Modules.Mana;

/// <summary>
/// The "gains mana on first mana-costing ability" hook -- called from AbilityGrantEffects.Grant
/// whenever the ability being granted has a nonzero ManaCost. A no-op if the entity already has a
/// ManaComponent (only the first mana-costing ability actually grants one) or if it has no
/// Intelligence AbilityScoreComponent yet (nothing sensible to size MaximumMana from -- callers
/// must grant ability scores before granting a mana-costing ability, the same ordering
/// PlayerBlueprint follows). MaximumMana is a one-time snapshot of Intelligence's Total at grant
/// time, not a value that tracks Intelligence forever after -- mirrors how HealthComponent.
/// MaximumHealth is baked once at blueprint-build time rather than recomputed live, with
/// StatModifierTarget.MaximumMana as the seam for anything (equipment, buffs) that wants to
/// adjust it afterward.
/// </summary>
public static class ManaGrant
{
    public static void EnsureManaComponentExists(ComponentManager componentManager, int entityId)
    {
        if (componentManager.GetPackedPool<ManaComponent>().Has(entityId))
        {
            return;
        }

        if (!AbilityScoreQueries.TryGetComponent(componentManager.GetMultiPool<AbilityScoreComponent>(), entityId, AbilityScoreType.Intelligence, out var intelligence))
        {
            return;
        }

        componentManager.Merge(entityId, new ManaComponent(intelligence.Total, intelligence.Total));
    }
}
