using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Poison.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Game.Modules.Poison.Systems;

/// <summary>
/// Ticks down each poisoned entity's countdown and, once it reaches 0, deals damage equal to
/// the current stack count. RemainingDurationTicks (independent of stack count) counts down,
/// and the whole effect is removed in one go once that reaches 0. The decrement-or-fire loop
/// itself is Engine.ECS.Systems.CountdownTicker.Tick, shared with BurningSystem/
/// ContactDamageSystem/StatusEffectAuraSystem -- this class only supplies the entity-id
/// source and what "ticking" actually does.
/// </summary>
public sealed class PoisonSystem : ISystem
{
    public byte StripeCount => 1;

    private readonly PackedComponentPool<PoisonTimerComponent> _timers;
    private readonly MultiComponentPool<StatusEffectStack> _stacks;
    private readonly PackedComponentPool<HealthComponent> _health;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly EventBus _eventBus;
    private readonly IPlayerQuery? _playerQuery;
    private readonly List<int> _pendingTimerRemovals = [];

    // Cached once instead of passing the Tick method group at the CountdownTicker.Tick call
    // site every Update -- see ContactDamageSystem's own field for why this matters (an
    // instance method group conversion allocates a fresh delegate every evaluation).
    private readonly Func<int, PoisonTimerComponent, bool> _tick;

    public PoisonSystem(
        PackedComponentPool<PoisonTimerComponent> timers,
        MultiComponentPool<StatusEffectStack> stacks,
        PackedComponentPool<HealthComponent> health,
        EventBus eventBus,
        IPlayerQuery? playerQuery,
        MultiComponentPool<StatModifierComponent>? statModifiers = null)
    {
        _timers = timers;
        _stacks = stacks;
        _health = health;
        _statModifiers = statModifiers;
        _eventBus = eventBus;
        _playerQuery = playerQuery;
        _tick = Tick;
    }

    public void Update(EngineTime time, byte stripeIndex) =>
        CountdownTicker.Tick(_timers, _timers.EntityIds, _pendingTimerRemovals, _tick);

    /// <summary>Returns whether the timer should be removed entirely (duration expired) -- see CountdownTicker.Tick's own doc comment for the contract. Drains every Poison stack itself before reporting removal, since that's a separate pool CountdownTicker knows nothing about (contrast BurningSystem, which only ever removes a single stack per tick, so it doesn't need this).</summary>
    private bool Tick(int entityId, PoisonTimerComponent timer)
    {
        if (timer.RemainingDurationTicks == 0)
        {
            RemoveAllStacks(entityId);
            return true;
        }

        HealthDamage.Apply(_health, _eventBus, entityId, timer.StackCount, timer.Source, _playerQuery, StatusEffectDamageType.Describe(StatusEffectType.Poison), _statModifiers);

        var remainingDuration = (ushort)(timer.RemainingDurationTicks - 1);
        if (remainingDuration == 0)
        {
            RemoveAllStacks(entityId);
            return true;
        }

        _timers.TryUpdate(entityId, remainingDuration, static (ref PoisonTimerComponent t, ushort remaining) =>
        {
            t.RemainingDurationTicks = remaining;
            t.FramesUntilNextTick = PoisonEffects.TickIntervalFrames;
        });

        return false;
    }

    /// <summary>Poison expires all at once -- every stack this entity has must be drained from the shared pool here, not just one (contrast BurningSystem, which only ever removes a single stack per tick).</summary>
    private void RemoveAllStacks(int entityId)
    {
        while (_stacks.RemoveFirst(entityId, static (ref readonly StatusEffectStack stack) => stack.EffectType == StatusEffectType.Poison))
        {
        }
    }
}
