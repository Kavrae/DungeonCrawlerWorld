using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.AbilityScores.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.AbilityScores;

/// <summary>The length of an entity's standard action lock: what every action, item and step locks it for unless it states its own duration.</summary>
/// <remarks>
/// Derived from the entity's Dexterity total each time it locks, then StatModifierTarget.ActionLockFrames
/// on top, rounded to the nearest frame. Nothing caches it, so a change to Dexterity or to a lock
/// modifier applies from the entity's next lock with nothing to keep in sync.
/// </remarks>
public static class StandardActionLockFrames
{
    /// <summary>0.5s @ GameFrameRate.FramesPerSecond -- the lock at Dexterity total 1, and the base for an entity with no Dexterity score.</summary>
    public const ushort MaximumLockFrames = 30;

    /// <summary>0.25s @ GameFrameRate.FramesPerSecond -- the lock at Dexterity total 300 (AbilityScoreMath's own clamp range).</summary>
    public const ushort MinimumLockFrames = 15;

    /// <summary>Linear ramp from MaximumLockFrames at Dexterity total 1 down to MinimumLockFrames at total 300, unrounded.</summary>
    public static float ComputeFromDexterity(ushort dexterityTotal) =>
        AbilityScoreMath.Lerp(dexterityTotal, MaximumLockFrames, MinimumLockFrames);

    /// <summary>The entity's standard lock: its Dexterity-derived length with its ActionLockFrames modifiers applied, at least 1 frame.</summary>
    public static ushort ResolveForEntity(PackedComponentPool<AbilityScoresComponent> abilityScores, MultiComponentPool<StatModifierComponent> statModifiers, int entityId) =>
        RoundStandardLock(StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.ActionLockFrames, DexterityLockFrames(abilityScores, entityId)));

    /// <summary>The lock a step sets, before the diagonal multiplier: the standard lock with the entity's MovementLockFrames modifiers applied on top.</summary>
    /// <remarks>Both modifier targets are read in one walk of the entity's modifier chain -- a step is the commonest lock there is.</remarks>
    public static ushort ResolveMovementForEntity(PackedComponentPool<AbilityScoresComponent> abilityScores, MultiComponentPool<StatModifierComponent> statModifiers, int entityId)
    {
        var (actionLock, movementLock) = StatModifierMath.GetSums(statModifiers, entityId, StatModifierTarget.ActionLockFrames, StatModifierTarget.MovementLockFrames);
        var standardLockFrames = RoundStandardLock(actionLock.ApplyTo(DexterityLockFrames(abilityScores, entityId)));
        return MathUtility.ClampUShort(movementLock.ApplyTo(standardLockFrames), 0, ushort.MaxValue);
    }

    private static float DexterityLockFrames(PackedComponentPool<AbilityScoresComponent> abilityScores, int entityId) =>
        AbilityScoreQueries.TryGetComponent(abilityScores, entityId, AbilityScoreType.Dexterity, out var dexterity)
            ? ComputeFromDexterity(dexterity.Total)
            : MaximumLockFrames;

    private static ushort RoundStandardLock(float modifiedLockFrames) => MathUtility.ClampUShort(MathF.Round(modifiedLockFrames), 1, ushort.MaxValue);

    /// <summary>explicitLockFrames when an action or item states its own lock, otherwise the entity's standard lock.</summary>
    public static ushort ResolveForEntity(PackedComponentPool<AbilityScoresComponent> abilityScores, MultiComponentPool<StatModifierComponent> statModifiers, int entityId, ushort? explicitLockFrames) =>
        explicitLockFrames ?? ResolveForEntity(abilityScores, statModifiers, entityId);
}
