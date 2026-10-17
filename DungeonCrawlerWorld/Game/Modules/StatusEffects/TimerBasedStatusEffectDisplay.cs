using Engine.ECS.Components.Stores;

namespace Game.Modules.StatusEffects;

/// <summary>
/// A single IStatusEffectDisplay implementation for the shape every current status effect
/// shares: remaining duration and stack count both live on a PackedComponentPool&lt;T&gt; timer
/// component. The variable part is a Func&lt;T, long, int&gt; extracting remaining-duration-in-frames
/// from the timer struct as of a given frame (timers store absolute deadlines -- see
/// FrameDeadline) -- register one of these per effect from that effect's own Configure instead of
/// writing a new class per effect (mirrors TimerBasedStatusEffectApplier&lt;T&gt;'s own shape, including its
/// IStatusEffectStackCount constraint -- GetStackCount reads T.StackCount generically the same way
/// TimerBasedStatusEffectApplier&lt;T&gt;.GetCurrentStackCount does).
/// </summary>
/// <param name="getRemainingDurationFrames">(timer, now) -> frames remaining as of now.</param>
/// <param name="getSource">The source the timer records; null for an effect whose timer records none.</param>
public sealed class TimerBasedStatusEffectDisplay<T>(StatusEffectType effectType, string glyph, PackedComponentPool<T> timers, Func<T, long, int> getRemainingDurationFrames, Func<T, Game.World.ActionSource>? getSource = null) : IStatusEffectDisplay where T : struct, IStatusEffectStackCount
{
    public Game.World.ActionSource? GetSource(int entityId) =>
        getSource is not null && timers.TryGetReadonly(entityId, out var timer) ? getSource(timer) : null;

    public StatusEffectType EffectType { get; } = effectType;
    public string Glyph { get; } = glyph;

    public int? GetRemainingDurationFrames(int entityId, long now) =>
        timers.TryGetReadonly(entityId, out var timer) ? getRemainingDurationFrames(timer, now) : null;

    public int GetStackCount(int entityId) =>
        timers.TryGetReadonly(entityId, out var timer) ? timer.StackCount : 0;
}
