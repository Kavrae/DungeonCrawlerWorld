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
/// <param name="applyStack">(entityId, source, now) -- see IStatusEffectAuraApplier.ApplyStack.</param>
public sealed class TimerBasedAuraApplier<T>(StatusEffectType effectType, PackedComponentPool<T> timers, Action<int, ActionSource, long> applyStack) : IStatusEffectAuraApplier where T : struct, IStatusEffectStackCount
{
    public StatusEffectType EffectType { get; } = effectType;

    public int GetCurrentStackCount(int entityId) =>
        timers.TryGetReadonly(entityId, out var timer) ? timer.StackCount : 0;

    public void ApplyStack(int entityId, ActionSource source, long now) =>
        applyStack(entityId, source, now);

    /// <remarks>Removing the timer component is the whole effect: its stack count lives on it, and the effect's timer wheel drops a removed timer's scheduled tick on its own.</remarks>
    public void RemoveAllStacks(int entityId) => timers.Remove(entityId);
}
