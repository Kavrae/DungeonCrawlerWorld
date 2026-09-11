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
/// ExpiringStatModifierComponent's own doc comment for the full reasoning. Base StripeCount 1, so
/// a Local-tier entity is visited every real frame. A throttled (Neighborhood/Borough/Beyond)
/// entity is visited every StripeCount * divisor frames, and each visit deducts that whole span
/// (framesPerVisit, via ITieredSystem.UpdateBucket), so its remaining duration still progresses
/// in real time -- only the granularity is coarser. This used to deduct a single frame per visit
/// regardless of tier, so a far-from-player buff or debuff outlasted its authored duration by the
/// tier's divisor; that was a defect, not the deliberate fidelity tradeoff this comment once
/// described it as.
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
public sealed class StatModifierExpirySystem : ITieredSystem
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

    public void Update(EngineTime time, byte stripeIndex) => TieredSystemRunner.Run(this, time);

    public TieredEntityStripeSet Tiers => _tieredStripeSet;

    /// <summary>One tier's due entities, scaled by that tier's framesPerVisit -- see ITieredSystem.UpdateBucket.</summary>
    public void UpdateBucket(EngineTime time, ReadOnlySpan<int> entityIds, ushort framesPerVisit)
    {
        foreach (var entityId in entityIds)
        {
            _pendingExpirations.Clear();

            for (var denseIndex = _statModifiers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _statModifiers.GetNextDenseIndex(denseIndex))
            {
                ref readonly var modifier = ref _statModifiers.GetReadonlyByDenseIndex(denseIndex);
                if (modifier.RemainingDurationFrames > 0)
                {
                    // "<= framesPerVisit", not "== 1": this visit covers that whole span, so any
                    // modifier with no more than that left is expiring now.
                    if (modifier.RemainingDurationFrames <= framesPerVisit)
                    {
                        _pendingExpirations.Add(modifier.Target);
                    }

                    _statModifiers.UpdateByDenseIndex(denseIndex, framesPerVisit, static (ref StatModifierComponent modifier, ushort frames) =>
                        modifier.RemainingDurationFrames = (ushort)System.Math.Max(0, modifier.RemainingDurationFrames!.Value - frames));
                }
            }

            while (_statModifiers.RemoveFirst(entityId, static (ref readonly StatModifierComponent modifier) => modifier.RemainingDurationFrames == 0))
            {
            }

            // One marker per modifier that just expired -- _pendingExpirations was collected
            // above from entries with no more than this visit's own span left, which are exactly
            // the non-permanent ones the while loop just removed (a permanent modifier's null
            // never satisfies either condition), so the counts line up 1:1.
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
