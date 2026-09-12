using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.StatusEffectAura.Components;

namespace Game.Modules.StatusEffectAura.Systems;

/// <summary>
/// On the frame an AuraSourceExpiryComponent expires, revokes that entity's aura source of the
/// expired Type (AuraSourceEffects.Revoke -- a targeted, unconditional remove, not a flip, so it
/// can't accidentally re-add an already-off source) and removes the expiry.
/// </summary>
/// <remarks>
/// Driven by a timer wheel (PackedTimerWheel), the same one-shot "always remove, no re-arm" shape
/// as ParalysisSystem: only expiries actually due are touched, at every processing tier
/// (PLAN-timer-wheel.md). AuraSourceGrant merging the component is all that schedules it.
/// </remarks>
public sealed class AuraSourceExpirySystem : ISystem
{
    private readonly MultiComponentPool<StatusEffectAuraSourceComponent> _sources;
    private readonly EventBus _eventBus;
    private readonly PackedTimerWheel<AuraSourceExpiryComponent> _wheel;
    private readonly TimerFired<AuraSourceExpiryComponent> _tick;

    public AuraSourceExpirySystem(
        PackedComponentPool<AuraSourceExpiryComponent> expiries,
        MultiComponentPool<StatusEffectAuraSourceComponent> sources,
        EventBus eventBus)
    {
        _sources = sources;
        _eventBus = eventBus;
        _tick = Tick;
        _wheel = new PackedTimerWheel<AuraSourceExpiryComponent>(expiries);
    }

    /// <summary>Every frame; the wheel only touches expiries actually due.</summary>
    public byte StripeCount => 1;

    public void Update(EngineTime time, byte stripeIndex) => _wheel.Tick(time.FrameCount, _tick);

    private bool Tick(int entityId, AuraSourceExpiryComponent expiry, long now)
    {
        AuraSourceEffects.Revoke(_sources, _eventBus, entityId, expiry.Type);
        return true;
    }
}
