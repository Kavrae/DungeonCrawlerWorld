using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Burning.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Game.Modules.Burning.Systems;

/// <summary>
/// Ticks down each burning entity's countdown and, once it reaches 0, deals damage equal to
/// the current stack count and removes exactly one stack (not per-stack damage -- 7 stacks
/// deals 7 damage total, not 49). The decrement-or-fire loop itself is
/// Engine.ECS.Systems.CountdownTicker.Tick, shared with PoisonSystem/ContactDamageSystem/
/// StatusEffectAuraSystem -- this class only supplies the entity-id source and what "ticking"
/// actually does.
/// </summary>
public sealed class BurningSystem : ISystem
{
    public byte StripeCount => 1;

    private readonly PackedComponentPool<BurningTimerComponent> _timers;
    private readonly MultiComponentPool<StatusEffectStack> _stacks;
    private readonly PackedComponentPool<HealthComponent> _health;
    private readonly EventBus _eventBus;
    private readonly IPlayerQuery? _playerQuery;
    private readonly List<int> _pendingTimerRemovals = [];

    public BurningSystem(
        PackedComponentPool<BurningTimerComponent> timers,
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
    }

    public void Update(EngineTime time, byte stripeIndex) =>
        CountdownTicker.Tick(_timers, _timers.EntityIds, _pendingTimerRemovals, Tick);

    /// <summary>Returns whether the timer should be removed entirely (stacks fully decayed) -- see CountdownTicker.Tick's own doc comment for the contract.</summary>
    private bool Tick(int entityId, BurningTimerComponent timer)
    {
        var stackCount = timer.StackCount;
        if (stackCount == 0)
        {
            return true;
        }

        var source = default(StatusEffectSource);
        var foundDenseIndex = -1;

        for (var denseIndex = _stacks.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _stacks.GetNextDenseIndex(denseIndex))
        {
            var stack = _stacks.GetReadonlyByDenseIndex(denseIndex);
            if (stack.EffectType == StatusEffectType.Burning)
            {
                foundDenseIndex = denseIndex;
                source = stack.Source;
                break;
            }
        }

        HealthDamage.Apply(_health, _eventBus, entityId, (short)stackCount, source, _playerQuery, StatusEffectDamageType.Describe(StatusEffectType.Burning));
        _stacks.RemoveByDenseIndex(foundDenseIndex);

        var remainingStacks = stackCount - 1;
        if (remainingStacks == 0)
        {
            return true;
        }

        _timers.TryUpdate(entityId, remainingStacks, static (ref BurningTimerComponent t, int remaining) =>
        {
            t.StackCount = remaining;
            t.FramesUntilNextTick = BurningEffects.TickIntervalFrames;
        });

        return false;
    }
}
