using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.StatusEffects.Components;

namespace Game.Modules.StatusEffects.Systems;

/// <summary>
/// Removes each timed StatusEffectImmunityComponent on the exact frame it expires, at every
/// processing tier. One MultiTimerWheel over the immunity pool does all of it: an immunity is
/// scheduled by the pool's own change notification when it is granted (see IScheduledTimer), its
/// single firing removes it, and a permanent immunity (FrameDeadline.Never) is never scheduled,
/// so entities carrying only permanent immunities -- the TreasureChest/Shop case -- cost nothing
/// at all rather than being visited forever. That replaces the tiered decrement-every-visit walk
/// this system used to be, along with its whole class of "the coarser the tier, the longer the
/// immunity outlasted its authored duration" defects (PLAN-timer-wheel.md).
/// </summary>
public sealed class StatusEffectImmunityExpirySystem : ISystem
{
    /// <summary>Every frame; the wheel only touches immunities actually due.</summary>
    public byte StripeCount => 1;

    /// <summary>Nothing to unwind -- the immunity's whole meaning is its presence, so expiry is removal. Cached once rather than allocated per Update.</summary>
    private static readonly TimerFired<StatusEffectImmunityComponent> Expire = static (entityId, immunity, now) => true;

    private readonly MultiTimerWheel<StatusEffectImmunityComponent> _wheel;

    public StatusEffectImmunityExpirySystem(MultiComponentPool<StatusEffectImmunityComponent> immunities) =>
        _wheel = new MultiTimerWheel<StatusEffectImmunityComponent>(immunities);

    public void Update(EngineTime time, byte stripeIndex) => _wheel.Tick(time.FrameCount, Expire);
}
