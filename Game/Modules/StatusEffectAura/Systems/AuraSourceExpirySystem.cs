using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatusEffectAura.Components;

namespace Game.Modules.StatusEffectAura.Systems;

/// <summary>
/// Ticks every active AuraSourceExpiryComponent down to 0 and, once it hits 0, revokes that
/// entity's aura source of the expired Type (AuraSourceEffects.Revoke -- a targeted, unconditional
/// remove, not a flip, so it can't accidentally re-add an already-off source) -- mirrors
/// ParalysisSystem's shape (a one-shot "always remove, no re-arm" CountdownTicker consumer,
/// tiered the same way): only entities actually carrying a timed grant are ever visited.
/// </summary>
public sealed class AuraSourceExpirySystem : ITieredSystem
{
    private const byte StripeCountValue = 15;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<AuraSourceExpiryComponent> _expiries;
    private readonly MultiComponentPool<StatusEffectAuraSourceComponent> _sources;
    private readonly TieredEntityStripeSet _tieredStripeSet;
    private readonly EventBus _eventBus;
    private readonly List<int> _pendingRemovals = [];
    private readonly Func<int, AuraSourceExpiryComponent, bool> _tick;

    public AuraSourceExpirySystem(
        PackedComponentPool<AuraSourceExpiryComponent> expiries,
        MultiComponentPool<StatusEffectAuraSourceComponent> sources,
        EventBus eventBus,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents)
    {
        _expiries = expiries;
        _sources = sources;
        _eventBus = eventBus;
        _tick = Tick;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, expiries, processingTiers, processingTierEvents);
    }

    /// <summary>Tiered rather than a whole-pool scan every frame -- see ParalysisSystem.Update's own note on why "this population stays small" is not an assumption worth resting on here.</summary>
    public void Update(EngineTime time, byte stripeIndex) => TieredSystemRunner.Run(this, time);

    public TieredEntityStripeSet Tiers => _tieredStripeSet;

    /// <summary>Advances each due entity's countdown by framesPerVisit -- see ITieredSystem.UpdateBucket.</summary>
    public void UpdateBucket(EngineTime time, ReadOnlySpan<int> entityIds, ushort framesPerVisit) =>
        CountdownTicker.Tick(_expiries, entityIds, _pendingRemovals, _tick, framesPerVisit);

    private bool Tick(int entityId, AuraSourceExpiryComponent expiry)
    {
        AuraSourceEffects.Revoke(_sources, _eventBus, entityId, expiry.Type);
        return true;
    }
}
