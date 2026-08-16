using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.StatModifiers.Systems;

/// <summary>
/// Ticks every active StatModifierComponent's RemainingDurationFrames down toward 0 and removes
/// it once it gets there -- a permanent modifier (RemainingDurationFrames == null)
/// never enters the decrement branch and never equals 0, so it's untouched forever. Because of
/// that, this system's TieredEntityStripeSet is driven off ExpiringStatModifierComponent
/// membership, not StatModifierComponent membership directly -- an entity holding only permanent
/// modifiers (most of the game's population, e.g. every Goblin's racial damage reduction) is
/// never due at all, rather than being visited every cycle just to find nothing to do. See
/// ExpiringStatModifierComponent's own doc comment for the full reasoning. StripeCount
/// 1 (visits every entity every real frame) for exact frame counting, the same reasoning
/// DelayedActionSystem/PoisonSystem use -- exact only for a Local-tier entity, though: a
/// throttled (Neighborhood/Borough/Beyond-tier) entity is visited less often via
/// TieredEntityStripeSet, so its remaining duration progresses more slowly in real time, the
/// same fidelity tradeoff every ProcessingTier consumer accepts for far-from-player entities
/// (see ProcessingTierSystem's own doc comment). A buff/debuff outlasting its nominal duration
/// on an off-screen entity is invisible until the player travels there, same as MovementSystem's
/// throttled wander pacing.
///
/// Two passes, not one, because RemoveFirst/RemoveByDenseIndex compact the *whole pool's* dense
/// array (swap-last-into-slot), which would corrupt an in-progress GetNextDenseIndex chain walk
/// if a removal happened mid-walk -- the same hazard CountdownTicker.Tick defers removals to
/// avoid, just not reusable here directly since CountdownTicker is PackedComponentPool-only and
/// this pool is Multi (several independent expiries per entity). Pass 1 only mutates in place
/// (UpdateByDenseIndex never moves entries) so it's safe to run the whole chain walk -- it also
/// collects the Target of any modifier about to hit 0 (RemainingDurationFrames == 1, i.e. this
/// decrement is its last) into a reused per-visit buffer, since pass 2's removal doesn't report
/// what it removed. Pass 2 then removes whatever hit 0, one at a time via RemoveFirst, mirroring
/// PoisonSystem's own RemoveAllStacks loop; StatModifierExpiredEvent is published afterward, once
/// removal is safely done, for each collected Target -- generic (any module can subscribe), not
/// just for AbilityScoresModule's benefit.
/// </summary>
public sealed class StatModifierExpirySystem : ISystem
{
    private const byte StripeCountValue = 1;

    public byte StripeCount => StripeCountValue;

    private readonly MultiComponentPool<StatModifierComponent> _statModifiers;
    private readonly MultiComponentPool<ExpiringStatModifierComponent> _expiringMarkers;
    private readonly EventBus _eventBus;
    private readonly TieredEntityStripeSet _tieredStripeSet;
    private readonly List<StatModifierTarget> _pendingExpirations = [];

    public StatModifierExpirySystem(
        MultiComponentPool<StatModifierComponent> statModifiers,
        MultiComponentPool<ExpiringStatModifierComponent> expiringMarkers,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        EventBus eventBus)
    {
        _statModifiers = statModifiers;
        _expiringMarkers = expiringMarkers;
        _eventBus = eventBus;

        // Driven off expiringMarkers, not statModifiers -- see ExpiringStatModifierComponent's
        // own doc comment. statModifiers is still what Update actually walks below (a due
        // entity's permanent and temporary modifiers live in the same chain), this just
        // controls which entities are ever due at all.
        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, expiringMarkers, processingTiers, processingTierEvents);
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _tieredStripeSet.GetDueEntities(time.FrameCount))
        {
            _pendingExpirations.Clear();

            for (var denseIndex = _statModifiers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _statModifiers.GetNextDenseIndex(denseIndex))
            {
                ref readonly var modifier = ref _statModifiers.GetReadonlyByDenseIndex(denseIndex);
                if (modifier.RemainingDurationFrames > 0)
                {
                    if (modifier.RemainingDurationFrames == 1)
                    {
                        _pendingExpirations.Add(modifier.Target);
                    }

                    _statModifiers.UpdateByDenseIndex(denseIndex, static (ref StatModifierComponent modifier) => modifier.RemainingDurationFrames--);
                }
            }

            while (_statModifiers.RemoveFirst(entityId, static (ref readonly StatModifierComponent modifier) => modifier.RemainingDurationFrames == 0))
            {
            }

            // One marker per modifier that just expired -- _pendingExpirations was collected
            // above from RemainingDurationFrames == 1 entries only, which are exactly the
            // non-permanent ones the while loop just removed (a permanent modifier's null never
            // satisfies either condition), so the counts line up 1:1.
            for (var i = 0; i < _pendingExpirations.Count; i++)
            {
                _expiringMarkers.RemoveFirst(entityId, static (ref readonly ExpiringStatModifierComponent _) => true);
            }

            foreach (var target in _pendingExpirations)
            {
                _eventBus.Publish(new StatModifierExpiredEvent(entityId, target));
            }
        }
    }
}
