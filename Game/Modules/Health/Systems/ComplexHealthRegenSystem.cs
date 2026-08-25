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

/// <summary>Complex-health counterpart to SimpleHealthRegenSystem -- regenerates one body part per due entity per visit, adjusting for ability scores, modifiers, and processing tier.</summary>
/// <remarks>
/// Two differences from SimpleHealthRegenSystem: BodyPartSelection.PickLowestPercentage picks
/// which of the entity's parts gets this visit's regen (instead of unconditionally updating a
/// single pool), and every visit to a due entity also walks that entity's own BodyPartComponent
/// chain once to decrement any nonzero RegenLockoutFramesRemaining, regardless of whether a part
/// was selected for healing this tick.
/// </remarks>
public sealed class ComplexHealthRegenSystem : ISystem
{
    public byte StripeCount => (byte)GameTiming.FramesPerSecond;

    /// <summary>Flat HP/sec at Constitution total 1 -- matches SimpleHealthRegenSystem's own placeholder constant.</summary>
    private const float MinHealthRegenPerSecond = 2f;

    /// <summary>Flat HP/sec at Constitution total 300.</summary>
    private const float MaxHealthRegenPerSecond = 6f;

    private readonly MultiComponentPool<BodyPartComponent> _bodyParts;
    private readonly DirectComponentPool<ProcessingTierComponent> _processingTiers;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly MultiComponentPool<AbilityScoreComponent>? _abilityScores;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public ComplexHealthRegenSystem(
        MultiComponentPool<BodyPartComponent> bodyParts,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        MultiComponentPool<AbilityScoreComponent>? abilityScores = null)
    {
        _bodyParts = bodyParts;
        _processingTiers = processingTiers;
        _statModifiers = statModifiers;
        _deadEntities = deadEntities;
        _abilityScores = abilityScores;

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

            var selectedDenseIndex = BodyPartSelection.PickLowestPercentage(_bodyParts, entityId, _statModifiers);
            if (selectedDenseIndex == -1)
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

            _bodyParts.UpdateByDenseIndex(selectedDenseIndex, (EffectiveRegen: effectiveRegen, _statModifiers, entityId), static (ref BodyPartComponent part, (float EffectiveRegen, MultiComponentPool<StatModifierComponent>? StatModifiers, int EntityId) state) =>
            {
                var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(state.StatModifiers, state.EntityId, StatModifierTarget.MaximumHealth, part.MaximumHealth);
                part.CurrentHealth = MathHelper.Clamp(part.CurrentHealth + state.EffectiveRegen, 0f, effectiveMaximumHealth);

                if (part.CurrentHealth > 0)
                {
                    part.IsDisabled = false;
                }
            });
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
