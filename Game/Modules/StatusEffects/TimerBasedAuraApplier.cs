using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.World;

namespace Game.Modules.StatusEffects;

/// <summary>
/// A single IStatusEffectAuraApplier implementation for the shape every current status effect
/// shares: stack count lives on a PackedComponentPool&lt;T&gt; timer component, and applying a
/// stack is one static ApplyStack-shaped call. Replaces a hand-written per-effect class
/// (BurningAuraApplier, PoisonAuraApplier) that differed only in T and in the applyStack
/// delegate itself (Poison's needs an extra durationInTicks argument, supplied by wrapping it
/// in a lambda at the registration call site -- see PoisonModule.Configure) -- register one of
/// these per effect from that effect's own Configure instead of writing a new class per effect.
/// </summary>
public sealed class TimerBasedAuraApplier<T> : IStatusEffectAuraApplier where T : struct, IStatusEffectStackCount
{
    public StatusEffectType EffectType { get; }

    private readonly Action<ComponentManager, int, StatusEffectSource> _applyStack;
    private PackedComponentPool<T>? _timers;

    public TimerBasedAuraApplier(StatusEffectType effectType, Action<ComponentManager, int, StatusEffectSource> applyStack)
    {
        EffectType = effectType;
        _applyStack = applyStack;
    }

    /// <summary>Cached lazily, not at construction: Configure (where this is built) runs before any module's RegisterComponents, so componentManager's pools don't exist yet -- by the time this is actually called, componentManager is the same stable instance for the life of the game.</summary>
    public int GetCurrentStackCount(ComponentManager componentManager, int entityId)
    {
        _timers ??= componentManager.GetPackedPool<T>();
        return _timers.TryGetReadonly(entityId, out var timer) ? timer.StackCount : 0;
    }

    public void ApplyStack(ComponentManager componentManager, int entityId, StatusEffectSource source) =>
        _applyStack(componentManager, entityId, source);
}
