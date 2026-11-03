using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
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
public sealed class ManaRegenSystem : ITieredSystem
{
    public byte StripeCount => (byte)GameFrameRate.FramesPerSecond;

    private readonly PackedComponentPool<ManaComponent> _manaComponents;
    private readonly MultiComponentPool<StatModifierComponent> _statModifiers;
    private readonly PackedComponentPool<DeadComponent> _deadEntities;
    private readonly PackedComponentPool<AbilityScoresComponent> _abilityScores;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public ManaRegenSystem(
        PackedComponentPool<ManaComponent> manaComponents,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        MultiComponentPool<StatModifierComponent> statModifiers,
        PackedComponentPool<DeadComponent> deadEntities,
        PackedComponentPool<AbilityScoresComponent> abilityScores)
    {
        _manaComponents = manaComponents;
        _statModifiers = statModifiers;
        _deadEntities = deadEntities;
        _abilityScores = abilityScores;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, manaComponents, processingTiers, processingTierEvents);
    }

    public void Update(EngineTime time, byte stripeIndex) => TieredSystemRunner.Run(this, time);

    public TieredEntityStripeSet Tiers => _tieredStripeSet;

    /// <summary>One tier's due entities, scaled by that tier's framesPerVisit -- see ITieredSystem.UpdateBucket.</summary>
    public void UpdateBucket(EngineTime time, ReadOnlySpan<int> entityIds, ushort framesPerVisit)
    {
        // Reused across every due entity in this bucket, not re-stackalloc'd per entity -- each
        // iteration overwrites both entries before reading them.
        Span<(StatModifierTarget Target, float BaseValue)> pairs = stackalloc (StatModifierTarget, float)[2];
        Span<float> effectiveValues = stackalloc float[2];
        var secondsPerVisit = framesPerVisit / (float)GameFrameRate.FramesPerSecond;

        foreach (var entityId in entityIds)
        {
            var manaDenseIndex = _manaComponents.GetDenseIndex(entityId);
            if (manaDenseIndex < 0)
            {
                continue;
            }

            if (_deadEntities.Has(entityId))
            {
                continue;
            }

            if (!ManaRegeneration.TryGetBasePerSecond(_abilityScores, entityId, out var basePerSecond))
            {
                continue;
            }

            pairs[0] = (StatModifierTarget.MaximumMana, _manaComponents.GetReadonlyByDenseIndex(manaDenseIndex).MaximumMana);
            pairs[1] = (StatModifierTarget.ManaRegen, basePerSecond);
            StatModifierMath.GetEffectiveValues(_statModifiers, entityId, pairs, effectiveValues);
            var effectiveMaximumMana = effectiveValues[0];
            var effectiveRegen = effectiveValues[1] * secondsPerVisit;

            if (effectiveRegen == 0f)
            {
                continue;
            }

            _manaComponents.UpdateByDenseIndex(manaDenseIndex, new ShiftCurrentManaUpdate(effectiveRegen, effectiveMaximumMana));
        }
    }
}
