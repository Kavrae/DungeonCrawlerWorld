using Engine.ECS.Components.Stores;
using Game.Modules.StatusEffects.Components;

namespace Game.Modules.StatusEffects;

/// <summary>Shared read helpers over the StatusEffectStack pool, used by every effect's own system and by Presentation rendering alike.</summary>
/// <remarks>
/// MultiComponentPool has no built-in "which distinct field values does this entity's chain
/// contain" or "match/count by field X" accessors -- an entity may hold several stacks (one per
/// active StatusEffectType, sometimes more than one of the same type), so the pool only exposes
/// a generic dense-chain walk plus predicate-based helpers (TryGetFirst/CountMatching),
/// deliberately blind to what StatusEffectType means. This class owns that match/aggregate logic
/// in one place instead of every caller (systems and Presentation alike) re-walking the chain
/// itself.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class StatusEffectQueries
{
    private static readonly StatusEffectType[] AllEffectTypes = Enum.GetValues<StatusEffectType>();

    /// <summary>Fills destination with every distinct StatusEffectType entityId currently has at least one stack of.</summary>
    /// <remarks>
    /// Return in enum declaration order (stable frame to frame, so a caller drawing them left-to-right doesn't
    /// see them reshuffle). Fills destination rather than allocating.
    /// </remarks>
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

    /// <summary>Determines whether the specified entity has at least one stack of the given effect type.</summary>
    /// <param name="stacks">The pool of status effect stacks.</param>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="effectType">The type of effect to look for.</param>
    /// <returns>True if the entity has at least one stack of the given effect type, false otherwise.</returns>
    public static bool HasStack(MultiComponentPool<StatusEffectStack> stacks, int entityId, StatusEffectType effectType) =>
        stacks.TryGetFirst(entityId, effectType, static (ref readonly StatusEffectStack stack, StatusEffectType type) => stack.EffectType == type, out _);

    /// <summary>Counts the number of stacks of the given effect type that the specified entity has.</summary>
    /// <param name="stacks">The pool of status effect stacks.</param>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="effectType">The type of effect to count.</param>
    /// <returns>The number of stacks of the given effect type that the entity has.</returns>
    public static int CountStacks(MultiComponentPool<StatusEffectStack> stacks, int entityId, StatusEffectType effectType) =>
        stacks.CountMatching(entityId, effectType, static (ref readonly StatusEffectStack stack, StatusEffectType type) => stack.EffectType == type);
}
