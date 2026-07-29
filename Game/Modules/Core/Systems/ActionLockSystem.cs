using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Core.Components;

namespace Game.Modules.Core.Systems;

/// <summary>
/// Passively counts every entity's shared action lock down toward 0, once per stripe cycle.
/// Action-gating systems only ever read LockFramesRemaining and set it (via ActionLockGate.Lock,
/// which also sets TotalLockFrames) when an action is taken. TotalLockFrames is never touched
/// here -- it stays as whatever Lock last set it to, for consumers (e.g. a cooldown UI) that
/// need "how much of the lock is left, as a fraction of the whole."
/// StripeCount is 10 (not smaller, e.g. the 3 briefly used for finer real-time player-cadence
/// tuning) to match HealthRegenSystem's population-safe convention -- at TestMapBuilder's
/// ~178k-creature scale, StripeCount=3 measurably cost ~2.7ms/frame more than StripeCount=10
/// (roughly 59k vs 18k entities visited per frame), a real, measured performance regression.
/// </summary>
public sealed class ActionLockSystem : ISystem
{
    private const byte StripeCountValue = 10;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly EntityStripeSet _stripeSet;

    public ActionLockSystem(PackedComponentPool<ActionLockComponent> actionLocks)
    {
        _actionLocks = actionLocks;
        _stripeSet = new EntityStripeSet(StripeCount, actionLocks.EntityIds);
        actionLocks.EntityAdded += _stripeSet.OnEntityAdded;
        actionLocks.EntityRemoved += _stripeSet.OnEntityRemoved;
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _stripeSet.GetBucket(stripeIndex))
        {
            if (_actionLocks.TryGetReadonly(entityId, out var actionLock) && actionLock.LockFramesRemaining != 0)
            {
                _actionLocks.TryUpdate(entityId, static (ref ActionLockComponent a) =>
                {
                    a.LockFramesRemaining = (short)Math.Max(0, a.LockFramesRemaining - StripeCountValue);
                });
            }
        }
    }
}
