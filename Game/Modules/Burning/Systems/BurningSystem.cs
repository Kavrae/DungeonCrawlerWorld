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
/// deals 7 damage total, not 49).
///
/// StripeCount is deliberately 1, not the 10 EnergyRechargeSystem/HealthRegenSystem use:
/// SystemManager.Update calls this system's Update once per real frame regardless, but a
/// striped system (StripeCount > 1) only visits a given entity once every StripeCount frames
/// -- fine for Energy/Health, whose populations are large enough that striping's cost-bounding
/// matters, wrong here: a naive per-visit decrement would make "60 frames" actually take 600
/// real frames at StripeCount=10. Burning's population (entities currently on fire) is
/// expected to stay small, so processing all of it every frame (StripeCount=1, one bucket)
/// keeps the countdown a plain per-real-frame decrement instead.
///
/// Tick reads its stack count from BurningTimerComponent.StackCount (see that field's doc
/// comment) rather than walking the entity's whole StatusEffectStack chain to count -- that
/// chain is shared across every effect type an entity has, so counting-by-walking here would
/// mean every effect's own system re-scans the same mixed chain looking for its own type on
/// every tick. Only one dense-index lookup is still needed: something has to point at *a*
/// Burning-typed entry to remove and to read its source off of, so Tick still walks the chain,
/// but stops at the first match instead of walking it in full.
/// </summary>
public sealed class BurningSystem : ISystem
{
    public byte StripeCount => 1;

    private readonly PackedComponentPool<BurningTimerComponent> _timers;
    private readonly MultiComponentPool<StatusEffectStack> _stacks;
    private readonly PackedComponentPool<HealthComponent> _health;
    private readonly EventBus _eventBus;
    private readonly IPlayerQuery? _playerQuery;
    private readonly EntityStripeSet _stripeSet;

    // Reused across Update calls rather than allocated fresh each frame. Entries whose timer
    // should be removed are collected here during the foreach below and removed only after it
    // completes -- EntityStripeSet.GetBucket returns a Span over the live backing list, and
    // removing an entity mid-foreach fires EntityRemoved -> EntityStripeSet.OnEntityRemoved,
    // which swap-removes within the very bucket currently being enumerated (silent skip/reread
    // risk otherwise).
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
                _timers.TryUpdate(entityId, static (ref BurningTimerComponent t) => t.FramesUntilNextTick--);
                continue;
            }

            Tick(entityId, timer.StackCount);
        }

        foreach (var entityId in _pendingTimerRemovals)
        {
            _timers.Remove(entityId);
        }
    }

    private void Tick(int entityId, int stackCount)
    {
        if (stackCount == 0)
        {
            _pendingTimerRemovals.Add(entityId);
            return;
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
            _pendingTimerRemovals.Add(entityId);
        }
        else
        {
            _timers.TryUpdate(entityId, remainingStacks, static (ref BurningTimerComponent t, int remaining) =>
            {
                t.StackCount = remaining;
                t.FramesUntilNextTick = BurningEffects.TickIntervalFrames;
            });
        }
    }
}
