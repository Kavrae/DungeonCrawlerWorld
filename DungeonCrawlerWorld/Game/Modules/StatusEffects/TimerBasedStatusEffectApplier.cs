using Engine.ECS.Components.Stores;
using Game.World;

namespace Game.Modules.StatusEffects;

/// <summary>Applies stacks of one status effect: (entityId, count, source, now, announcesRefusal, immunityAlreadyChecked), returning how many landed and whether an immunity refused them -- see IStatusEffectApplier.ApplyStacks.</summary>
public delegate StatusEffectStacksApplied StatusEffectStackApplication(int entityId, int count, ActionSource source, long now, bool announcesRefusal, bool immunityAlreadyChecked);

/// <summary>
/// A single IStatusEffectApplier implementation for the shape every current status effect
/// shares: stack count lives on a PackedComponentPool&lt;T&gt; timer component, and applying
/// stacks is one static ApplyStacks-shaped call. Replaces a hand-written per-effect class that
/// differed only in T and in the applyStacks delegate itself (Poison's needs an extra
/// durationInTicks argument, supplied by wrapping it in a lambda at the registration call site --
/// see PoisonModule.Configure) -- register one of these per effect from that effect's own
/// Configure instead of writing a new class per effect. Held on the entity as a whole: a body part
/// named by a grant is ignored.
/// </summary>
public sealed class TimerBasedStatusEffectApplier<T>(StatusEffectType effectType, PackedComponentPool<T> timers, int maxStacks, StatusEffectStackApplication applyStacks) : IStatusEffectApplier where T : struct, IStatusEffectStackCount
{
    public StatusEffectType EffectType { get; } = effectType;

    public int MaxStacks { get; } = maxStacks;

    public int GetCurrentStackCount(int entityId, byte? bodyPartId = null) =>
        timers.TryGetReadonly(entityId, out var timer) ? timer.StackCount : 0;

    public StatusEffectStacksApplied ApplyStacks(int entityId, int count, ActionSource source, long now, bool announcesRefusal = true, byte? bodyPartId = null, bool immunityAlreadyChecked = false) =>
        applyStacks(entityId, count, source, now, announcesRefusal, immunityAlreadyChecked);

    /// <remarks>Removing the timer component is the whole effect: its stack count lives on it, and the effect's timer wheel drops a removed timer's scheduled tick on its own.</remarks>
    public bool RemoveAllStacks(int entityId) => timers.Remove(entityId);
}
