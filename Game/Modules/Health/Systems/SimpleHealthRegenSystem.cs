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
public sealed class SimpleHealthRegenSystem : ITieredSystem
{
    public byte StripeCount => (byte)GameTiming.FramesPerSecond;

    /// <summary>Flat HP/sec at Constitution total 1 -- adjustable in a later balance pass, same as every other placeholder stat-scaling constant in this codebase.</summary>
    private const float MinHealthRegenPerSecond = 2f;

    /// <summary>Flat HP/sec at Constitution total 300.</summary>
    private const float MaxHealthRegenPerSecond = 6f;

    private readonly PackedComponentPool<SimpleHealthComponent> _healthComponents;
    private readonly EntityBodyParts _bodyParts;
    private readonly MultiComponentPool<StatModifierComponent> _statModifiers;
    private readonly PackedComponentPool<DeadComponent> _deadEntities;
    private readonly PackedComponentPool<AbilityScoresComponent> _abilityScores;
    private readonly EventBus _eventBus;
    private readonly IPlayerQuery _playerQuery;
    private readonly FloatingTextFeed _floatingTextFeed;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public SimpleHealthRegenSystem(
        PackedComponentPool<SimpleHealthComponent> healthComponents,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        MultiComponentPool<StatModifierComponent> statModifiers,
        PackedComponentPool<DeadComponent> deadEntities,
        PackedComponentPool<AbilityScoresComponent> abilityScores,
        EntityBodyParts bodyParts,
        EventBus eventBus,
        IPlayerQuery playerQuery,
        FloatingTextFeed floatingTextFeed)
    {
        _healthComponents = healthComponents;
        _statModifiers = statModifiers;
        _deadEntities = deadEntities;
        _abilityScores = abilityScores;
        _bodyParts = bodyParts;
        _eventBus = eventBus;
        _playerQuery = playerQuery;
        _floatingTextFeed = floatingTextFeed;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, healthComponents, processingTiers, processingTierEvents);
    }

    /// <summary>Updates the current health of all entities in the current stripe by the regen amount, routed through HealthHeal.Apply (sourceEntityId: entityId, a self-heal) so a regen tick carries Outgoing/IncomingHealing modifiers the same way any other heal does.</summary>
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
            Regenerate(entityId, secondsPerVisit, time.FrameCount);
        }
    }

    private void Regenerate(int entityId, float seconds, long now)
    {
        // A corpse shouldn't regenerate back above 0.
        if (_deadEntities.Has(entityId))
        {
            return;
        }

        // This entity never got a Constitution score (e.g. a non-creature SimpleHealthComponent
        // holder) -- 0 regen, same as the effectiveRegen == 0 skip below, just resolved a step earlier.
        if (!AbilityScoreQueries.TryGetComponent(_abilityScores, entityId, AbilityScoreType.Constitution, out var constitution))
        {
            return;
        }

        var amountPerSecond = AbilityScoreMath.Lerp(constitution.Total, MinHealthRegenPerSecond, MaxHealthRegenPerSecond);
        var rawAmount = amountPerSecond * seconds;
        var effectiveRegen = StatModifierMath.GetEffectiveValue(_statModifiers, entityId, StatModifierTarget.HealthRegen, rawAmount);

        if (effectiveRegen == 0f)
        {
            return;
        }

        HealthHeal.Apply(_healthComponents, entityId, percentOfMaxHealth: 0f, now, _statModifiers, _bodyParts, flatAmount: effectiveRegen, sourceEntityId: entityId, eventBus: _eventBus, playerQuery: _playerQuery, floatingTextFeed: _floatingTextFeed, healCategory: HealCategory.Regeneration, healType: "Regeneration");
    }
}
