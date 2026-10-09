using Engine.ECS.Components;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.StatModifiers.Components;

namespace Tests;

/// <summary>An entity's standard action lock as the game resolves it, for a test that reads it off a ComponentManager.</summary>
internal static class TestActionLocks
{
    public static ushort StandardLockOf(ComponentManager componentManager, int entityId) =>
        StandardActionLockFrames.ResolveForEntity(componentManager.GetPackedPool<AbilityScoresComponent>(), componentManager.GetMultiPool<StatModifierComponent>(), entityId);
}
