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

/// <summary>Regenerates entity current and maximum health, adjusting for ability scores, modifiers, and processing tier.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class SimpleHealthRegenSystem : ISystem
{
    public byte StripeCount => (byte)GameTiming.FramesPerSecond;

    /// <summary>Flat HP/sec at Constitution total 1 -- adjustable in a later balance pass, same as every other placeholder stat-scaling constant in this codebase.</summary>
    private const float MinHealthRegenPerSecond = 2f;

    /// <summary>Flat HP/sec at Constitution total 300.</summary>
    private const float MaxHealthRegenPerSecond = 6f;

    private readonly PackedComponentPool<SimpleHealthComponent> _healthComponents;
    private readonly DirectComponentPool<ProcessingTierComponent> _processingTiers;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly MultiComponentPool<AbilityScoreComponent>? _abilityScores;
    private readonly EventBus? _eventBus;
    private readonly IPlayerQuery? _playerQuery;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public SimpleHealthRegenSystem(
        PackedComponentPool<SimpleHealthComponent> healthComponents,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        MultiComponentPool<AbilityScoreComponent>? abilityScores = null,
        EventBus? eventBus = null,
        IPlayerQuery? playerQuery = null)
    {
        _healthComponents = healthComponents;
        _processingTiers = processingTiers;
        _statModifiers = statModifiers;
        _deadEntities = deadEntities;
        _abilityScores = abilityScores;
        _eventBus = eventBus;
        _playerQuery = playerQuery;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, healthComponents, processingTiers, processingTierEvents);
    }

    /// <summary>Updates the current health of all entities in the current stripe by the regen amount, routed through HealthHeal.Apply (sourceEntityId: entityId, a self-heal) so a regen tick carries Outgoing/IncomingHealing modifiers the same way any other heal does.</summary>
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

            // No AbilityScoresModule loaded, or this entity never got a Constitution score
            // (e.g. a non-creature SimpleHealthComponent holder) -- 0 regen, same as today's
            // effectiveRegen == 0 skip below, just resolved a step earlier.
            if (_abilityScores is null || !AbilityScoreQueries.TryGetComponent(_abilityScores, entityId, AbilityScoreType.Constitution, out var constitution))
            {
                continue;
            }

            var tier = _processingTiers.TryGetReadonly(entityId, out var processingTier) ? processingTier.Tier : ProcessingTierLevel.Local;
            var framesPerVisit = StripeCount * ProcessingTierDivisors.ByTierIndex[(int)tier];
            var secondsPerVisit = framesPerVisit / (float)GameTiming.FramesPerSecond;

            var amountPerSecond = AbilityScoreMath.Lerp(constitution.Total, MinHealthRegenPerSecond, MaxHealthRegenPerSecond);
            var rawAmount = amountPerSecond * secondsPerVisit;
            var effectiveRegen = StatModifierMath.GetEffectiveValue(_statModifiers, entityId, StatModifierTarget.HealthRegen, rawAmount);

            if (effectiveRegen == 0f)
            {
                continue;
            }

            HealthHeal.Apply(_healthComponents, entityId, percentOfMaxHealth: 0f, _statModifiers, flatAmount: effectiveRegen, sourceEntityId: entityId, eventBus: _eventBus, playerQuery: _playerQuery, healType: "Regeneration");
        }
    }
}
