using Engine.ECS.Components.Stores;
using Game.Modules.StatusEffects.Components;

namespace Game.Modules.StatusEffects;

/// <summary>
/// Shared read helpers over the StatusEffectStack pool, used by every effect's own system and
/// by Presentation rendering alike -- iterates the pool's existing dense per-entity chain
/// </summary>
public static class StatusEffectQueries
{
    private static readonly StatusEffectType[] AllEffectTypes = Enum.GetValues<StatusEffectType>();

    /// <summary>
    /// Every distinct StatusEffectType entityId currently has at least one stack of, in enum
    /// declaration order (stable frame to frame, so a caller drawing them left-to-right doesn't
    /// see them reshuffle). Fills destination rather than allocating, the same reused-buffer
    /// style BurningSystem's own per-frame bookkeeping uses.
    /// </summary>
    public static void GetActiveEffectTypes(MultiComponentPool<StatusEffectStack> stacks, int entityId, List<StatusEffectType> destination)
    {
        destination.Clear();

        foreach (var effectType in AllEffectTypes)
        {
            if (HasStack(stacks, entityId, effectType))
            {
                destination.Add(effectType);
            }
        }
    }

    public static bool HasStack(MultiComponentPool<StatusEffectStack> stacks, int entityId, StatusEffectType effectType)
    {
        for (var denseIndex = stacks.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = stacks.GetNextDenseIndex(denseIndex))
        {
            if (stacks.GetReadonlyByDenseIndex(denseIndex).EffectType == effectType)
            {
                return true;
            }
        }

        return false;
    }

    public static int CountStacks(MultiComponentPool<StatusEffectStack> stacks, int entityId, StatusEffectType effectType)
    {
        var count = 0;

        for (var denseIndex = stacks.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = stacks.GetNextDenseIndex(denseIndex))
        {
            if (stacks.GetReadonlyByDenseIndex(denseIndex).EffectType == effectType)
            {
                count++;
            }
        }

        return count;
    }
}
