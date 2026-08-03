using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.StatModifiers.Systems;

/// <summary>
/// Ticks every active StatModifierComponent's RemainingDurationFrames down toward 0 and removes
/// it once it gets there -- a permanent modifier (RemainingDurationFrames == Permanent, -1)
/// never enters the decrement branch and never equals 0, so it's untouched forever. StripeCount
/// 1 (visits every entity every real frame) for exact frame counting, the same reasoning
/// DelayedActionSystem/PoisonSystem use.
///
/// Two passes, not one, because RemoveFirst/RemoveByDenseIndex compact the *whole pool's* dense
/// array (swap-last-into-slot), which would corrupt an in-progress GetNextDenseIndex chain walk
/// if a removal happened mid-walk -- the same hazard CountdownTicker.Tick defers removals to
/// avoid, just not reusable here directly since CountdownTicker is PackedComponentPool-only and
/// this pool is Multi (several independent expiries per entity). Pass 1 only mutates in place
/// (UpdateByDenseIndex never moves entries) so it's safe to run the whole chain walk; pass 2
/// then removes whatever hit 0, one at a time via RemoveFirst, mirroring PoisonSystem's own
/// RemoveAllStacks loop.
/// </summary>
public sealed class StatModifierExpirySystem : ISystem
{
    private const byte StripeCountValue = 1;

    public byte StripeCount => StripeCountValue;

    private readonly MultiComponentPool<StatModifierComponent> _statModifiers;
    private readonly EntityStripeSet _stripeSet;

    public StatModifierExpirySystem(MultiComponentPool<StatModifierComponent> statModifiers)
    {
        _statModifiers = statModifiers;

        _stripeSet = new EntityStripeSet(StripeCount, statModifiers.EntityIds);
        statModifiers.EntityAdded += _stripeSet.OnEntityAdded;
        statModifiers.EntityRemoved += _stripeSet.OnEntityRemoved;
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _stripeSet.GetBucket(stripeIndex))
        {
            for (var denseIndex = _statModifiers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _statModifiers.GetNextDenseIndex(denseIndex))
            {
                if (_statModifiers.GetReadonlyByDenseIndex(denseIndex).RemainingDurationFrames > 0)
                {
                    _statModifiers.UpdateByDenseIndex(denseIndex, static (ref StatModifierComponent modifier) => modifier.RemainingDurationFrames--);
                }
            }

            while (_statModifiers.RemoveFirst(entityId, static (ref readonly StatModifierComponent modifier) => modifier.RemainingDurationFrames == 0))
            {
            }
        }
    }
}
