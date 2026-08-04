using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.Health.Systems;

/// <summary>
/// Passively regenerates entity health, bounded between 0 and the modifier-adjusted effective
/// MaximumHealth (see StatModifierMath's own doc comment for why this is recomputed here rather
/// than baked into HealthComponent itself). Regen amount is likewise the effective HealthRegen,
/// not the raw stored field, so a temporary regen debuff (e.g. -100%) actually stalls/reverses
/// regeneration for its duration.
/// TODO Health v2: split into per-body-part health once a real damage/status-effect system
/// exists to justify the added complexity.
/// </summary>
public sealed class HealthRegenSystem : ISystem
{
    public byte StripeCount => 10;

    private readonly PackedComponentPool<HealthComponent> _healthComponents;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public HealthRegenSystem(
        PackedComponentPool<HealthComponent> healthComponents,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null)
    {
        _healthComponents = healthComponents;
        _statModifiers = statModifiers;
        _deadEntities = deadEntities;

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

            var (effectiveRegenValue, effectiveMaximumHealthValue) = StatModifierMath.GetEffectiveValues(
                _statModifiers, entityId,
                StatModifierTarget.HealthRegen, currentHealthComponent.HealthRegen,
                StatModifierTarget.MaximumHealth, currentHealthComponent.MaximumHealth);
            var effectiveRegen = (int)effectiveRegenValue;
            if (effectiveRegen == 0)
            {
                continue;
            }

            _healthComponents.TryUpdate(entityId, (effectiveRegen, EffectiveMaximumHealth: (int)effectiveMaximumHealthValue), static (ref HealthComponent healthComponent, (int EffectiveRegen, int EffectiveMaximumHealth) state) =>
            {
                var regeneratedHealth = healthComponent.CurrentHealth + state.EffectiveRegen;
                healthComponent.CurrentHealth = (short)MathUtility.ClampInt(regeneratedHealth, 0, state.EffectiveMaximumHealth);
            });
        }
    }
}