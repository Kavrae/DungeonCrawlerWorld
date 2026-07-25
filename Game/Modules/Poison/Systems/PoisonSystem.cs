using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Poison.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Game.Modules.Poison.Systems;

/// <summary>
/// Ticks down each poisoned entity's countdown and, once it reaches 0, deals damage equal to
/// the current stack count.
/// RemainingDurationTicks (independent of stack count) counts down, and the whole effect is
/// removed in one go once that reaches 0.
public sealed class PoisonSystem : ISystem
{
    public byte StripeCount => 1;

    private readonly PackedComponentPool<PoisonTimerComponent> _timers;
    private readonly MultiComponentPool<StatusEffectStack> _stacks;
    private readonly PackedComponentPool<HealthComponent> _health;
    private readonly EventBus _eventBus;
    private readonly IPlayerQuery? _playerQuery;
    private readonly EntityStripeSet _stripeSet;

    private readonly List<int> _pendingTimerRemovals = [];

    public PoisonSystem(
        PackedComponentPool<PoisonTimerComponent> timers,
        MultiComponentPool<StatusEffectStack> stacks,
        PackedComponentPool<HealthComponent> health,
        EventBus eventBus,
        IPlayerQuery? playerQuery)
    {
        _timers = timers;
        _stacks = stacks;
        _health = health;
        _eventBus = eventBus;
        _playerQuery = playerQuery;
        _stripeSet = new EntityStripeSet(StripeCount, timers.EntityIds);
        timers.EntityAdded += _stripeSet.OnEntityAdded;
        timers.EntityRemoved += _stripeSet.OnEntityRemoved;
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        _pendingTimerRemovals.Clear();

        foreach (var entityId in _stripeSet.GetBucket(stripeIndex))
        {
            if (!_timers.TryGetReadonly(entityId, out var timer))
            {
                continue;
            }

            if (timer.FramesUntilNextTick > 1)
            {
                _timers.TryUpdate(entityId, static (ref PoisonTimerComponent t) => t.FramesUntilNextTick--);
                continue;
            }

            Tick(entityId, timer);
        }

        foreach (var entityId in _pendingTimerRemovals)
        {
            RemoveAllStacks(entityId);
            _timers.Remove(entityId);
        }
    }

    private void Tick(int entityId, PoisonTimerComponent timer)
    {
        HealthDamage.Apply(_health, _eventBus, entityId, (short)timer.StackCount, timer.Source, _playerQuery, StatusEffectDamageType.Describe(StatusEffectType.Poison));

        var remainingDuration = timer.RemainingDurationTicks - 1;
        if (remainingDuration <= 0)
        {
            _pendingTimerRemovals.Add(entityId);
        }
        else
        {
            _timers.TryUpdate(entityId, remainingDuration, static (ref PoisonTimerComponent t, int remaining) =>
            {
                t.RemainingDurationTicks = remaining;
                t.FramesUntilNextTick = PoisonEffects.TickIntervalFrames;
            });
        }
    }

    /// <summary>Poison expires all at once -- every stack this entity has must be drained from the shared pool here, not just one (contrast BurningSystem, which only ever removes a single stack per tick).</summary>
    private void RemoveAllStacks(int entityId)
    {
        while (_stacks.RemoveFirst(entityId, static (ref readonly StatusEffectStack stack) => stack.EffectType == StatusEffectType.Poison))
        {
        }
    }
}
