using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.StatusEffectAura.Components;

namespace Game.Modules.StatusEffectAura.Systems;

/// <summary>
/// Ticks every active AuraSourceExpiryComponent down to 0 and, once it hits 0, revokes that
/// entity's aura source of the expired Type (AuraSourceEffects.Revoke -- a targeted, unconditional
/// remove, not a flip, so it can't accidentally re-add an already-off source) -- mirrors
/// ParalysisSystem's shape (StripeCount 1, one-shot "always remove, no re-arm" CountdownTicker
/// consumer): only entities actually carrying a timed grant are ever visited.
/// </summary>
public sealed class AuraSourceExpirySystem : ISystem
{
    public byte StripeCount => 1;

    private readonly PackedComponentPool<AuraSourceExpiryComponent> _expiries;
    private readonly MultiComponentPool<StatusEffectAuraSourceComponent> _sources;
    private readonly EventBus _eventBus;
    private readonly List<int> _pendingRemovals = [];
    private readonly Func<int, AuraSourceExpiryComponent, bool> _tick;

    public AuraSourceExpirySystem(PackedComponentPool<AuraSourceExpiryComponent> expiries, MultiComponentPool<StatusEffectAuraSourceComponent> sources, EventBus eventBus)
    {
        _expiries = expiries;
        _sources = sources;
        _eventBus = eventBus;
        _tick = Tick;
    }

    public void Update(EngineTime time, byte stripeIndex) =>
        CountdownTicker.Tick(_expiries, _expiries.EntityIds, _pendingRemovals, _tick);

    private bool Tick(int entityId, AuraSourceExpiryComponent expiry)
    {
        AuraSourceEffects.Revoke(_sources, _eventBus, entityId, expiry.Type);
        return true;
    }
}
