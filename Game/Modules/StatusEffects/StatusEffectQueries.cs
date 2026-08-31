using Engine.ECS.Components;

namespace Game.Modules.StatusEffects;

/// <summary>Shared read helpers over the StatusEffectDisplayRegistry, used by every effect's own system and by Presentation rendering alike.</summary>
/// <remarks>
/// Dispatches by StatusEffectType through the registry each effect module registers an
/// IStatusEffectDisplay into during its own Configure -- no central switch over concrete effect
/// types, and no separate storage of "which effects are active": GetStackCount reads straight off
/// each effect's own timer component (see TimerBasedStatusEffectDisplay&lt;T&gt;), so there's
/// exactly one place the count lives.
/// </remarks>
/// <cleanupVersion>2</cleanupVersion>
public static class StatusEffectQueries
{
    private static readonly StatusEffectType[] AllEffectTypes = Enum.GetValues<StatusEffectType>();

    /// <summary>Fills destination with every distinct StatusEffectType entityId currently has at least one stack of.</summary>
    /// <remarks>
    /// Return in enum declaration order (stable frame to frame, so a caller drawing them left-to-right doesn't
    /// see them reshuffle). Fills destination rather than allocating.
    /// </remarks>
    public static void GetActiveEffectTypes(StatusEffectDisplayRegistry displays, ComponentManager componentManager, int entityId, List<StatusEffectType> destination)
    {
        destination.Clear();

        foreach (var effectType in AllEffectTypes)
        {
            if (HasStack(displays, componentManager, entityId, effectType))
            {
                destination.Add(effectType);
            }
        }
    }

    /// <summary>Determines whether the specified entity has at least one stack of the given effect type.</summary>
    public static bool HasStack(StatusEffectDisplayRegistry displays, ComponentManager componentManager, int entityId, StatusEffectType effectType) =>
        CountStacks(displays, componentManager, entityId, effectType) > 0;

    /// <summary>Counts the number of stacks of the given effect type that the specified entity has. 0 if the effect type has no registered display (e.g. Light, which has no module) or isn't active.</summary>
    public static int CountStacks(StatusEffectDisplayRegistry displays, ComponentManager componentManager, int entityId, StatusEffectType effectType) =>
        displays.TryGet(effectType, out var display) ? display.GetStackCount(componentManager, entityId) : 0;
}
