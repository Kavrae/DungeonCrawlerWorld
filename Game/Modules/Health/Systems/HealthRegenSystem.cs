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

/// <summary>
/// Passively regenerates entity health, bounded between 0 and the modifier-adjusted effective
/// MaximumHealth (see StatModifierMath's own doc comment for why this is recomputed here rather
/// than baked into HealthComponent itself). The regen amount is computed live every visit from
/// the entity's Constitution AbilityScoreComponent.Total via AbilityScoreRegenMath (2%/sec of max
/// at total 1, ramping to 6%/sec at total 300) -- no regen field is cached on HealthComponent, so
/// a Constitution buff/debuff is reflected the very next visit with no extra write-path needed to
/// keep a cached rate in sync. That percent-per-second rate is converted into a per-visit amount
/// using this entity's own tier cadence (StripeCount * ProcessingTierDivisors), since a coarser
/// tier's stripe comes due less often and needs a proportionally larger amount per visit to still
/// add up to the same rate over real time. The result is then layered with
/// StatModifierTarget.HealthRegen exactly the way MaximumHealth already is, so a direct regen
/// buff (e.g. Tank's class bonus, granted as a StatModifier rather than baked into a field) still
/// applies on top of the Constitution-derived base.
///
/// StripeCount is a full second's worth of frames (GameTiming.FramesPerSecond), not an arbitrary
/// small number -- ticking Local-tier entities every 60 frames instead of every 10 keeps each
/// visit's amount a full 1.0-second slice rather than a ~0.167s sliver, which matters for
/// bounding how many visits a second this system pays for, independent of the storage/rounding
/// question below.
///
/// The per-visit amount is added to HealthComponent.CurrentHealth (float) directly, with no
/// rounding at all -- HealthComponent's own doc comment covers why float storage exists. An
/// earlier version of this system rounded to a whole HP per visit (first plainly, then via
/// dithered/stochastic rounding once plain rounding was found to floor a low regen rate against
/// a small MaximumHealth to 0 forever); stochastic rounding fixed the "never regenerates" bug but
/// introduced its own real UX problem at low pool sizes -- an unlucky entity could still go
/// several visits without a single round-up, a visible stall right when a player expects enough
/// mana/health to have come back. Storing the exact float sidesteps the whole rounding question:
/// every visit's contribution is exact and immediate, no luck involved.
/// TODO Health v2: split into per-body-part health once a real damage/status-effect system
/// exists to justify the added complexity.
/// </summary>
public sealed class HealthRegenSystem : ISystem
{
    public byte StripeCount => (byte)GameTiming.FramesPerSecond;

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

    public void Update(EngineTime time, byte stripeIndex)
    {
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

            var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(_statModifiers, entityId, StatModifierTarget.MaximumHealth, currentHealthComponent.MaximumHealth);

            var tier = _processingTiers.TryGetReadonly(entityId, out var processingTier) ? processingTier.Tier : ProcessingTierLevel.Local;
            var framesPerVisit = StripeCount * ProcessingTierDivisors.ByTierIndex[(int)tier];
            var secondsPerVisit = framesPerVisit / (float)GameTiming.FramesPerSecond;

            var percentPerSecond = AbilityScoreRegenMath.ComputePercentPerSecond(constitution.Total);
            var rawAmount = percentPerSecond / 100f * effectiveMaximumHealth * secondsPerVisit;
            var effectiveRegen = StatModifierMath.GetEffectiveValue(_statModifiers, entityId, StatModifierTarget.HealthRegen, rawAmount);
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
