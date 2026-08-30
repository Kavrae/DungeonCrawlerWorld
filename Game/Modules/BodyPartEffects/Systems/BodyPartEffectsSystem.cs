using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Utilities;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;

namespace Game.Modules.BodyPartEffects.Systems;

/// <summary>
/// Translates Complex-health body-part condition into ordinary StatModifierComponent grants, so
/// MovementSystem/DirectDamage stay exactly as ignorant of body parts as every other stat
/// consumer -- see PLAN-body-part-gameplay-effects.md for the full design record.
/// </summary>
/// <remarks>
/// Each due entity's own Leg/Foot parts compound multiplicatively (per-part 1x at 100% HP up to
/// 2x at 0% HP) into a single StatModifierTarget.MovementLockFrames debuff; Arm/Hand parts
/// compound (per-part 1x down to 0x) into StatModifierTarget.MeleeOutgoingDamage. A non-disabled
/// Wing part suppresses the leg penalty (and the hard block below) entirely, checked before
/// either. Every Leg/Foot (or Arm/Hand) simultaneously disabled grants a hard-block marker
/// (MovementDisabledComponent/MeleeDisabledComponent) instead of just an extreme multiplier --
/// MovementSystem/ActionActivationSystem check those directly, the same way they already check
/// DeadComponent, so this stays the only place that reads BodyPartComponent for these effects.
/// </remarks>
public sealed class BodyPartEffectsSystem : ISystem
{
    /// <summary>Per-part multiplier at 0% HP -- 1x at 100% HP, compounding multiplicatively across every Leg/Foot the entity owns.</summary>
    private const float MaxMovementLockMultiplierPerPart = 2f;

    private const float ModifierMagnitudeEpsilon = 0.001f;

    public byte StripeCount => (byte)GameTiming.FramesPerSecond;

    private readonly MultiComponentPool<BodyPartComponent> _bodyParts;
    private readonly PackedComponentPool<MovementDisabledComponent> _movementDisabled;
    private readonly PackedComponentPool<MeleeDisabledComponent> _meleeDisabled;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public BodyPartEffectsSystem(
        MultiComponentPool<BodyPartComponent> bodyParts,
        PackedComponentPool<MovementDisabledComponent> movementDisabled,
        PackedComponentPool<MeleeDisabledComponent> meleeDisabled,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        MultiComponentPool<StatModifierComponent>? statModifiers = null)
    {
        _bodyParts = bodyParts;
        _movementDisabled = movementDisabled;
        _meleeDisabled = meleeDisabled;
        _statModifiers = statModifiers;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, bodyParts, processingTiers, processingTierEvents);
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _tieredStripeSet.GetDueEntities(time.FrameCount))
        {
            SyncLegPenalty(entityId);
            SyncArmPenalty(entityId);
        }
    }

    private void SyncLegPenalty(int entityId)
    {
        var hasFunctionalWing = false;
        var anyLegOrFoot = false;
        var allDisabled = true;
        var combinedMultiplier = 1f;

        for (var denseIndex = _bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref _bodyParts.GetReadonlyByDenseIndex(denseIndex);

            if (part.Type == BodyPartType.Wing && !part.IsDisabled)
            {
                hasFunctionalWing = true;
                break;
            }

            if (part.Type is not (BodyPartType.Leg or BodyPartType.Foot))
            {
                continue;
            }

            anyLegOrFoot = true;
            allDisabled &= part.IsDisabled;
            combinedMultiplier *= MaxMovementLockMultiplierPerPart - HealthFraction(part) * (MaxMovementLockMultiplierPerPart - 1f);
        }

        // A winged entity flies regardless of its own Leg/Foot condition -- checked before the
        // hard-block gate below, so both legs disabled still doesn't block a winged entity.
        if (!anyLegOrFoot || hasFunctionalWing)
        {
            _movementDisabled.Remove(entityId);
            RemoveModifier(entityId, StatModifierTarget.MovementLockFrames);
            return;
        }

        if (allDisabled)
        {
            if (!_movementDisabled.Has(entityId))
            {
                _movementDisabled.Add(entityId, default);
            }

            RemoveModifier(entityId, StatModifierTarget.MovementLockFrames);
            return;
        }

        _movementDisabled.Remove(entityId);
        SyncModifier(entityId, StatModifierTarget.MovementLockFrames, combinedMultiplier);
    }

    private void SyncArmPenalty(int entityId)
    {
        var anyArmOrHand = false;
        var allDisabled = true;
        var combinedMultiplier = 1f;

        for (var denseIndex = _bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref _bodyParts.GetReadonlyByDenseIndex(denseIndex);

            if (part.Type is not (BodyPartType.Arm or BodyPartType.Hand))
            {
                continue;
            }

            anyArmOrHand = true;
            allDisabled &= part.IsDisabled;
            combinedMultiplier *= HealthFraction(part);
        }

        if (!anyArmOrHand)
        {
            _meleeDisabled.Remove(entityId);
            RemoveModifier(entityId, StatModifierTarget.MeleeOutgoingDamage);
            return;
        }

        if (allDisabled)
        {
            if (!_meleeDisabled.Has(entityId))
            {
                _meleeDisabled.Add(entityId, default);
            }

            RemoveModifier(entityId, StatModifierTarget.MeleeOutgoingDamage);
            return;
        }

        _meleeDisabled.Remove(entityId);
        SyncModifier(entityId, StatModifierTarget.MeleeOutgoingDamage, combinedMultiplier);
    }

    private static float HealthFraction(in BodyPartComponent part) =>
        part.MaximumHealth > 0 ? MathHelper.Clamp(part.CurrentHealth / part.MaximumHealth, 0f, 1f) : 0f;

    /// <summary>Grants/updates/removes this system's own permanent multiplicative StatModifierComponent for target, so its effective value equals baseValue * combinedMultiplier (see StatModifierMath's own additive-then-multiplicative formula) -- StatModifierComponent's fields are get-only, so an actual change always means remove-then-re-add rather than an in-place magnitude edit.</summary>
    private void SyncModifier(int entityId, StatModifierTarget target, float combinedMultiplier)
    {
        if (_statModifiers is null)
        {
            return;
        }

        var desiredMagnitude = combinedMultiplier - 1f;
        var existingDenseIndex = FindModifierDenseIndex(entityId, target, out var existingMagnitude);

        if (System.Math.Abs(desiredMagnitude) <= ModifierMagnitudeEpsilon)
        {
            if (existingDenseIndex != -1)
            {
                _statModifiers.RemoveByDenseIndex(existingDenseIndex);
            }

            return;
        }

        if (existingDenseIndex != -1)
        {
            if (System.Math.Abs(existingMagnitude - desiredMagnitude) <= ModifierMagnitudeEpsilon)
            {
                return;
            }

            _statModifiers.RemoveByDenseIndex(existingDenseIndex);
        }

        _statModifiers.Add(entityId, new StatModifierComponent(
            target, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, canModify: false, desiredMagnitude, remainingDurationFrames: null, StatusEffectSource.Admin));
    }

    private void RemoveModifier(int entityId, StatModifierTarget target)
    {
        if (_statModifiers is null)
        {
            return;
        }

        var denseIndex = FindModifierDenseIndex(entityId, target, out _);
        if (denseIndex != -1)
        {
            _statModifiers.RemoveByDenseIndex(denseIndex);
        }
    }

    /// <summary>Target alone is enough to identify "this system's own grant" -- MovementLockFrames/MeleeOutgoingDamage exist solely for this system, nothing else ever grants against them.</summary>
    private int FindModifierDenseIndex(int entityId, StatModifierTarget target, out float magnitude)
    {
        for (var denseIndex = _statModifiers!.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _statModifiers.GetNextDenseIndex(denseIndex))
        {
            ref readonly var modifier = ref _statModifiers.GetReadonlyByDenseIndex(denseIndex);
            if (modifier.Target == target)
            {
                magnitude = modifier.Magnitude;
                return denseIndex;
            }
        }

        magnitude = 0f;
        return -1;
    }
}
