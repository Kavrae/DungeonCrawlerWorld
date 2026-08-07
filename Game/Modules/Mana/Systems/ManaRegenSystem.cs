using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Utilities;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Death.Components;
using Game.Modules.Mana.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Microsoft.Xna.Framework;

namespace Game.Modules.Mana.Systems;

/// <summary>
/// Mirrors HealthRegenSystem exactly (see its own doc comment for the full rationale, including
/// why StripeCount is a full second's worth of frames and why the per-visit amount is added to
/// CurrentMana as an exact float with no rounding at all -- Mana is in fact the case that made
/// float storage necessary: MaximumMana is typically only 2-12 for a starting roll, where even a
/// well-designed rounding scheme either stalls regen entirely (plain rounding) or produces
/// visible multi-tick dry streaks (stochastic rounding) -- see ManaComponent's own doc comment),
/// with Intelligence in place of Constitution and ManaComponent/StatModifierTarget.ManaRegen in
/// place of their Health equivalents. Only ever processes entities that already have a
/// ManaComponent -- ManaGrant.EnsureManaComponentExists is what grants one in the first place (on
/// an entity's first mana-costing ability), this system never grants one itself.
/// </summary>
public sealed class ManaRegenSystem : ISystem
{
    public byte StripeCount => (byte)GameTiming.FramesPerSecond;

    private readonly PackedComponentPool<ManaComponent> _manaComponents;
    private readonly DirectComponentPool<ProcessingTierComponent> _processingTiers;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly MultiComponentPool<AbilityScoreComponent>? _abilityScores;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public ManaRegenSystem(
        PackedComponentPool<ManaComponent> manaComponents,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        MultiComponentPool<AbilityScoreComponent>? abilityScores = null)
    {
        _manaComponents = manaComponents;
        _processingTiers = processingTiers;
        _statModifiers = statModifiers;
        _deadEntities = deadEntities;
        _abilityScores = abilityScores;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, manaComponents, processingTiers, processingTierEvents);
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _tieredStripeSet.GetDueEntities(time.FrameCount))
        {
            if (!_manaComponents.TryGetReadonly(entityId, out var currentManaComponent))
            {
                continue;
            }

            if (_deadEntities?.Has(entityId) == true)
            {
                continue;
            }

            if (_abilityScores is null || !AbilityScoreQueries.TryGetComponent(_abilityScores, entityId, AbilityScoreType.Intelligence, out var intelligence))
            {
                continue;
            }

            var effectiveMaximumMana = StatModifierMath.GetEffectiveValue(_statModifiers, entityId, StatModifierTarget.MaximumMana, currentManaComponent.MaximumMana);

            var tier = _processingTiers.TryGetReadonly(entityId, out var processingTier) ? processingTier.Tier : ProcessingTierLevel.Local;
            var framesPerVisit = StripeCount * ProcessingTierDivisors.ByTierIndex[(int)tier];
            var secondsPerVisit = framesPerVisit / (float)GameTiming.FramesPerSecond;

            var percentPerSecond = AbilityScoreRegenMath.ComputePercentPerSecond(intelligence.Total);
            var rawAmount = percentPerSecond / 100f * effectiveMaximumMana * secondsPerVisit;
            var effectiveRegen = StatModifierMath.GetEffectiveValue(_statModifiers, entityId, StatModifierTarget.ManaRegen, rawAmount);
            if (effectiveRegen == 0f)
            {
                continue;
            }

            _manaComponents.TryUpdate(entityId, (effectiveRegen, effectiveMaximumMana), static (ref ManaComponent manaComponent, (float EffectiveRegen, float EffectiveMaximumMana) state) =>
            {
                manaComponent.CurrentMana = MathHelper.Clamp(manaComponent.CurrentMana + state.EffectiveRegen, 0f, state.EffectiveMaximumMana);
            });
        }
    }
}
