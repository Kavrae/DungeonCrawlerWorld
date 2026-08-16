using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Utilities;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Microsoft.Xna.Framework;

namespace Game.Modules.Health.Systems;

/// <summary>Regenerates entity current and maximum health, adjusting for ability scores, modifiers, and processing tier.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class HealthRegenSystem : ISystem
{
    public byte StripeCount => (byte)GameTiming.FramesPerSecond;

    /// <summary>Flat HP/sec at Constitution total 1 -- adjustable in a later balance pass, same as every other placeholder stat-scaling constant in this codebase.</summary>
    private const float MinHealthRegenPerSecond = 2f;

    /// <summary>Flat HP/sec at Constitution total 300.</summary>
    private const float MaxHealthRegenPerSecond = 6f;

    private readonly PackedComponentPool<HealthComponent> _healthComponents;
    private readonly DirectComponentPool<ProcessingTierComponent> _processingTiers;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly MultiComponentPool<AbilityScoreComponent>? _abilityScores;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public HealthRegenSystem(
        PackedComponentPool<HealthComponent> healthComponents,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        MultiComponentPool<AbilityScoreComponent>? abilityScores = null)
    {
        _healthComponents = healthComponents;
        _processingTiers = processingTiers;
        _statModifiers = statModifiers;
        _deadEntities = deadEntities;
        _abilityScores = abilityScores;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, healthComponents, processingTiers, processingTierEvents);
    }

    /// <summary>Updates the current health of all entities in the current stripe by the regen amount.</summary>
    /// <param name="time"></param>
    /// <param name="stripeIndex"></param>
    public void Update(EngineTime time, byte stripeIndex)
    {
        // Reused across every due entity in this Update call, not re-stackalloc'd per entity --
        // each iteration overwrites both entries before reading them.
        Span<(StatModifierTarget Target, float BaseValue)> pairs = stackalloc (StatModifierTarget, float)[2];
        Span<float> effectiveValues = stackalloc float[2];

        foreach (var entityId in _tieredStripeSet.GetDueEntities(time.FrameCount))
        {
            if (!_healthComponents.TryGetReadonly(entityId, out var currentHealthComponent))
            {
                continue;
            }

            // A corpse shouldn't regenerate back above 0.
            if (_deadEntities?.Has(entityId) == true)
            {
                continue;
            }

            // No AbilityScoresModule loaded, or this entity never got a Constitution score
            // (e.g. a non-creature HealthComponent holder) -- 0 regen, same as today's
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

            pairs[0] = (StatModifierTarget.MaximumHealth, currentHealthComponent.MaximumHealth);
            pairs[1] = (StatModifierTarget.HealthRegen, rawAmount);
            StatModifierMath.GetEffectiveValues(_statModifiers, entityId, pairs, effectiveValues);
            var effectiveMaximumHealth = effectiveValues[0];
            var effectiveRegen = effectiveValues[1];

            if (effectiveRegen == 0f)
            {
                continue;
            }

            _healthComponents.TryUpdate(entityId, (effectiveRegen, effectiveMaximumHealth), static (ref HealthComponent healthComponent, (float EffectiveRegen, float EffectiveMaximumHealth) state) =>
            {
                healthComponent.CurrentHealth = MathHelper.Clamp(healthComponent.CurrentHealth + state.EffectiveRegen, 0f, state.EffectiveMaximumHealth);
            });
        }
    }
}
