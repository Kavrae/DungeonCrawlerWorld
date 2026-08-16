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


/// <summary>Regenerates entity current and maximum mana, adjusting for ability scores, modifiers, and processing tier.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ManaRegenSystem : ISystem
{
    public byte StripeCount => (byte)GameTiming.FramesPerSecond;

    private const float MinManaRegenPerSecond = 0.1f;
    private const float MaxManaRegenPerSecond = 0.3f;

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
        // Reused across every due entity in this Update call, not re-stackalloc'd per entity --
        // each iteration overwrites both entries before reading them.
        Span<(StatModifierTarget Target, float BaseValue)> pairs = stackalloc (StatModifierTarget, float)[2];
        Span<float> effectiveValues = stackalloc float[2];

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

            var tier = _processingTiers.TryGetReadonly(entityId, out var processingTier) ? processingTier.Tier : ProcessingTierLevel.Local;
            var framesPerVisit = StripeCount * ProcessingTierDivisors.ByTierIndex[(int)tier];
            var secondsPerVisit = framesPerVisit / (float)GameTiming.FramesPerSecond;

            var amountPerSecond = AbilityScoreMath.Lerp(intelligence.Total, MinManaRegenPerSecond, MaxManaRegenPerSecond);
            var rawAmount = amountPerSecond * secondsPerVisit;

            pairs[0] = (StatModifierTarget.MaximumMana, currentManaComponent.MaximumMana);
            pairs[1] = (StatModifierTarget.ManaRegen, rawAmount);
            StatModifierMath.GetEffectiveValues(_statModifiers, entityId, pairs, effectiveValues);
            var effectiveMaximumMana = effectiveValues[0];
            var effectiveRegen = effectiveValues[1];

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
