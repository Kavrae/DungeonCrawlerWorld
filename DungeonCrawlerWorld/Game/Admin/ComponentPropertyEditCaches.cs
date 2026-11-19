using Engine.ECS.Properties;
using Game.Bootstrap;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;

namespace Game.Admin;

/// <summary>Admin Mode's edits, built with every value a component caches from others recomputed after an edit or revert of what it is made from.</summary>
/// <remarks>Every other stat is read live from its base and stat modifiers, so an edit shows at once; an ability score's total is stored (AbilityScoresComponent), so an edit of its base recomputes it. A stat modifier's amount is read-only in its table and its expiry recomputes the total through the modifier's own expiry.</remarks>
public static class ComponentPropertyEditCaches
{
    public static ComponentPropertyEdits For(GameSession gameSession)
    {
        var componentManager = gameSession.EcsContext.ComponentManager;
        var edits = new ComponentPropertyEdits(componentManager, gameSession.EcsContext.SystemManager, gameSession.SimulationClock);
        edits.RefreshAfterWrite<AbilityScoresComponent>(entityId => AbilityScoreEffects.RecomputeAll(componentManager, entityId));
        return edits;
    }
}
