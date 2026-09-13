using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Utilities;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.Health.Systems;

/// <summary>Complex-health counterpart to SimpleHealthRegenSystem -- regenerates one body part per due entity per visit, adjusting for ability scores, modifiers, and processing tier.</summary>
/// <remarks>
/// Routes each visit through HealthHeal.Apply (targetMode: LowestPercentage, sourceEntityId:
/// entityId -- a self-heal), which is what BodyPartSelection.PickLowestPercentage/the
/// bodyPartBurningTimers exclusion actually run against now (ComplexHealthHeal.ApplyToSinglePart
/// -- see its own doc comment); this system no longer picks a part or mutates health itself.
/// Requires a SimpleHealthComponent pool purely to satisfy HealthHeal.Apply's Simple-vs-Complex
/// dispatch check -- every entity this system's own stripe set drives owns BodyPartComponent, so
/// that check always resolves to the Complex branch, mirroring ComplexHealthDamage.Apply's
/// identical requirement. The regen lockout costs this system nothing per visit: it is a deadline
/// on each part (BodyPartComponent.RegenLockedUntilFrame), consulted only when a part is being
/// considered for healing, rather than a countdown this system had to walk every part to advance.
/// </remarks>
public sealed class ComplexHealthRegenSystem : ITieredSystem
{
    public byte StripeCount => (byte)GameTiming.FramesPerSecond;

    /// <summary>Flat HP/sec at Constitution total 1 -- matches SimpleHealthRegenSystem's own placeholder constant.</summary>
    private const float MinHealthRegenPerSecond = 2f;

    /// <summary>Flat HP/sec at Constitution total 300.</summary>
    private const float MaxHealthRegenPerSecond = 6f;

    private readonly MultiComponentPool<BodyPartComponent> _bodyParts;
    private readonly PackedComponentPool<SimpleHealthComponent> _health;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly MultiComponentPool<AbilityScoreComponent>? _abilityScores;
    private readonly MultiComponentPool<BodyPartBurningTimerComponent>? _bodyPartBurningTimers;
    private readonly EventBus? _eventBus;
    private readonly IPlayerQuery? _playerQuery;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public ComplexHealthRegenSystem(
        MultiComponentPool<BodyPartComponent> bodyParts,
        PackedComponentPool<SimpleHealthComponent> health,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        MultiComponentPool<AbilityScoreComponent>? abilityScores = null,
        MultiComponentPool<BodyPartBurningTimerComponent>? bodyPartBurningTimers = null,
        EventBus? eventBus = null,
        IPlayerQuery? playerQuery = null)
    {
        _bodyParts = bodyParts;
        _health = health;
        _statModifiers = statModifiers;
        _deadEntities = deadEntities;
        _abilityScores = abilityScores;
        _bodyPartBurningTimers = bodyPartBurningTimers;
        _eventBus = eventBus;
        _playerQuery = playerQuery;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, bodyParts, processingTiers, processingTierEvents);
    }

    /// <summary>Updates the selected body part's current health, and decrements every part's regen lockout, for all entities in the current stripe.</summary>
    /// <param name="time"></param>
    /// <param name="stripeIndex"></param>
    public void Update(EngineTime time, byte stripeIndex) => TieredSystemRunner.Run(this, time);

    public TieredEntityStripeSet Tiers => _tieredStripeSet;

    /// <summary>One tier's due entities, scaled by that tier's framesPerVisit -- see ITieredSystem.UpdateBucket.</summary>
    public void UpdateBucket(EngineTime time, ReadOnlySpan<int> entityIds, ushort framesPerVisit)
    {
        var secondsPerVisit = framesPerVisit / (float)GameTiming.FramesPerSecond;

        foreach (var entityId in entityIds)
        {
            // A corpse shouldn't regenerate back above 0.
            if (_deadEntities?.Has(entityId) == true)
            {
                continue;
            }

            // No per-part lockout walk here any more: the lockout is a deadline that
            // BodyPartSelection.PickLowestPercentage compares against the current frame, so nothing
            // has to visit a part for its lockout to end (PLAN-timer-wheel.md step 8).

            // No AbilityScoresModule loaded, or this entity never got a Constitution score --
            // 0 regen, same as SimpleHealthRegenSystem's own effectiveRegen == 0 skip below, just
            // resolved a step earlier.
            if (_abilityScores is null || !AbilityScoreQueries.TryGetComponent(_abilityScores, entityId, AbilityScoreType.Constitution, out var constitution))
            {
                continue;
            }

            var amountPerSecond = AbilityScoreMath.Lerp(constitution.Total, MinHealthRegenPerSecond, MaxHealthRegenPerSecond);
            var rawAmount = amountPerSecond * secondsPerVisit;
            var effectiveRegen = StatModifierMath.GetEffectiveValue(_statModifiers, entityId, StatModifierTarget.HealthRegen, rawAmount);

            if (effectiveRegen == 0f)
            {
                continue;
            }

            HealthHeal.Apply(_health, entityId, percentOfMaxHealth: 0f, time.FrameCount, _statModifiers, _bodyParts, flatAmount: effectiveRegen, sourceEntityId: entityId, targetMode: BodyPartTargetMode.LowestPercentage, bodyPartBurningTimers: _bodyPartBurningTimers, eventBus: _eventBus, playerQuery: _playerQuery, healType: "Regeneration");
        }
    }
}
