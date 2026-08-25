using Engine.ECS.Components;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Mana.Components;

namespace Game.Modules.Mana;

/// <summary>
/// The "gains mana on first mana-costing action" hook -- called from ActionGrantEffects.Grant
/// whenever the action being granted has a nonzero ManaCost. A no-op if the entity already has a
/// ManaComponent (only the first mana-costing action actually grants one) or if it has no
/// Intelligence AbilityScoreComponent yet (nothing sensible to size MaximumMana from -- callers
/// must grant ability scores before granting a mana-costing action, the same ordering
/// PlayerBlueprint follows). MaximumMana is a one-time snapshot of Intelligence's Total at grant
/// time, not a value that tracks Intelligence forever after -- mirrors how SimpleHealthComponent.
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
