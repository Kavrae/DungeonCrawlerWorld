using Engine.ECS.Components;
using Engine.ECS.Components.Stores;

namespace Game.Modules.StatusEffects;

/// <summary>
/// A single IStatusEffectDisplay implementation for the shape every current status effect
/// shares: remaining duration and stack count both live on a PackedComponentPool&lt;T&gt; timer
/// component. The variable part is a Func&lt;T, int&gt; extracting remaining-duration-in-frames
/// from the timer struct -- register one of these per effect from that effect's own Configure
/// instead of writing a new class per effect (mirrors TimerBasedAuraApplier&lt;T&gt;'s own shape,
/// including its IStatusEffectStackCount constraint -- GetStackCount reads T.StackCount
/// generically the same way TimerBasedAuraApplier&lt;T&gt;.GetCurrentStackCount does).
/// </summary>
public sealed class TimerBasedStatusEffectDisplay<T> : IStatusEffectDisplay where T : struct, IStatusEffectStackCount
{
    public StatusEffectType EffectType { get; }
    public string Glyph { get; }

    private readonly Func<T, int> _getRemainingDurationFrames;
    private PackedComponentPool<T>? _timers;

    public TimerBasedStatusEffectDisplay(StatusEffectType effectType, string glyph, Func<T, int> getRemainingDurationFrames)
    {
        EffectType = effectType;
        Glyph = glyph;
        _getRemainingDurationFrames = getRemainingDurationFrames;
    }

    /// <summary>Cached lazily, not at construction: Configure (where this is built) runs before any module's RegisterComponents, so componentManager's pools don't exist yet -- by the time this is actually called, componentManager is the same stable instance for the life of the game.</summary>
    public int? GetRemainingDurationFrames(ComponentManager componentManager, int entityId)
    {
        _timers ??= componentManager.GetPackedPool<T>();
        return _timers.TryGetReadonly(entityId, out var timer) ? _getRemainingDurationFrames(timer) : null;
    }

    public int GetStackCount(ComponentManager componentManager, int entityId)
    {
        _timers ??= componentManager.GetPackedPool<T>();
        return _timers.TryGetReadonly(entityId, out var timer) ? timer.StackCount : 0;
    }
}
