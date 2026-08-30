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
/// identical requirement. Every visit to a due entity also walks that entity's own
/// BodyPartComponent chain once to decrement any nonzero RegenLockoutFramesRemaining, regardless
/// of whether a part was selected for healing this tick.
/// </remarks>
public sealed class ComplexHealthRegenSystem : ISystem
{
    public byte StripeCount => (byte)GameTiming.FramesPerSecond;

    /// <summary>Flat HP/sec at Constitution total 1 -- matches SimpleHealthRegenSystem's own placeholder constant.</summary>
    private const float MinHealthRegenPerSecond = 2f;

    /// <summary>Flat HP/sec at Constitution total 300.</summary>
    private const float MaxHealthRegenPerSecond = 6f;

    private readonly MultiComponentPool<BodyPartComponent> _bodyParts;
    private readonly PackedComponentPool<SimpleHealthComponent> _health;
    private readonly DirectComponentPool<ProcessingTierComponent> _processingTiers;
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
        _processingTiers = processingTiers;
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
    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _tieredStripeSet.GetDueEntities(time.FrameCount))
        {
            // A corpse shouldn't regenerate back above 0.
            if (_deadEntities?.Has(entityId) == true)
            {
                continue;
            }

            var tier = _processingTiers.TryGetReadonly(entityId, out var processingTier) ? processingTier.Tier : ProcessingTierLevel.Local;
            var framesPerVisit = StripeCount * ProcessingTierDivisors.ByTierIndex[(int)tier];

            DecrementLockouts(entityId, framesPerVisit);

            // No AbilityScoresModule loaded, or this entity never got a Constitution score --
            // 0 regen, same as SimpleHealthRegenSystem's own effectiveRegen == 0 skip below, just
            // resolved a step earlier.
            if (_abilityScores is null || !AbilityScoreQueries.TryGetComponent(_abilityScores, entityId, AbilityScoreType.Constitution, out var constitution))
            {
                continue;
            }

            var secondsPerVisit = framesPerVisit / (float)GameTiming.FramesPerSecond;
            var amountPerSecond = AbilityScoreMath.Lerp(constitution.Total, MinHealthRegenPerSecond, MaxHealthRegenPerSecond);
            var rawAmount = amountPerSecond * secondsPerVisit;
            var effectiveRegen = StatModifierMath.GetEffectiveValue(_statModifiers, entityId, StatModifierTarget.HealthRegen, rawAmount);

            if (effectiveRegen == 0f)
            {
                continue;
            }

            HealthHeal.Apply(_health, entityId, percentOfMaxHealth: 0f, _statModifiers, _bodyParts, flatAmount: effectiveRegen, sourceEntityId: entityId, targetMode: BodyPartTargetMode.LowestPercentage, bodyPartBurningTimers: _bodyPartBurningTimers, eventBus: _eventBus, playerQuery: _playerQuery, healType: "Regeneration");
        }
    }

    /// <summary>Walks entityId's own BodyPartComponent chain once, decrementing any nonzero RegenLockoutFramesRemaining by framesPerVisit (clamped at 0) -- mutated in place via UpdateByDenseIndex, never removed, so walking and updating the same chain in one pass is safe.</summary>
    private void DecrementLockouts(int entityId, int framesPerVisit)
    {
        for (var denseIndex = _bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref _bodyParts.GetReadonlyByDenseIndex(denseIndex);
            if (part.RegenLockoutFramesRemaining == 0)
            {
                continue;
            }

            var decrementAmount = (ushort)System.Math.Min((int)part.RegenLockoutFramesRemaining, framesPerVisit);
            _bodyParts.UpdateByDenseIndex(denseIndex, decrementAmount, static (ref BodyPartComponent p, ushort amount) => p.RegenLockoutFramesRemaining -= amount);
        }
    }
}
