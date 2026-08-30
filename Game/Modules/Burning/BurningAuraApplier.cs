using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Burning.Components;
using Game.Modules.ContactDamage.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Game.Modules.Burning;

/// <summary>
/// Dispatching IStatusEffectAuraApplier for Burning: grants a body-part-scoped burn (a single,
/// resolved part) when the target is a Complex entity currently exposed to a contact-damage
/// hazard (e.g. standing in lava), otherwise grants today's entity-scoped BurningTimerComponent
/// exactly as before (BurningEffects.ApplyStack, unchanged).
/// </summary>
/// <remarks>
/// Hazard exposure is read from the target's own ContactDamageExposureComponent (if
/// ContactDamageModule is loaded), not from `source` -- StatusEffectAuraSystem.GrantStacks always
/// attributes an aura-granted stack to StatusEffectSource.Admin (its own doc comment explains why:
/// a position-aggregated grid can't cheaply recover which specific source contributed), so `source`
/// alone can never identify "this grant came from standing on lava," which is today's only real
/// Burning-granting aura source. `source.EntityId` is also checked, for a future direct (non-aura)
/// hazard-attributed grant -- harmless today since Lava's own grant never takes that path.
/// The same hazard-resolved BodyPartType/Bottommost rule ContactDamageSystem's own direct-contact
/// damage already uses is reused here (DamageOnContactComponent.PreferredTargetType), so a burning
/// part and a contact-damaged part read as the same "where a hazard hits" rule.
/// </remarks>
public sealed class BurningAuraApplier(MathUtility mathUtility) : IStatusEffectAuraApplier
{
    public StatusEffectType EffectType => StatusEffectType.Burning;

    private PackedComponentPool<BurningTimerComponent>? _entityTimers;
    private MultiComponentPool<BodyPartComponent>? _bodyParts;
    private MultiComponentPool<BodyPartBurningTimerComponent>? _bodyPartTimers;
    private MultiComponentPool<BodyPartStatusEffectStack>? _bodyPartStacks;
    private PackedComponentPool<ContactDamageExposureComponent>? _contactExposures;
    private PackedComponentPool<DamageOnContactComponent>? _hazards;
    private bool _poolsResolved;

    public int GetCurrentStackCount(ComponentManager componentManager, int entityId)
    {
        EnsurePools(componentManager);

        if (TryResolveHazard(entityId, StatusEffectSource.Admin, out var preferredType) && _bodyParts!.Has(entityId))
        {
            var partId = ResolveTargetPartId(entityId, preferredType);
            if (partId is not { } resolvedPartId)
            {
                return 0;
            }

            var timerDenseIndex = FindBodyPartTimer(entityId, resolvedPartId);
            return timerDenseIndex == -1 ? 0 : _bodyPartTimers!.GetReadonlyByDenseIndex(timerDenseIndex).StackCount;
        }

        return _entityTimers!.TryGetReadonly(entityId, out var timer) ? timer.StackCount : 0;
    }

    public void ApplyStack(ComponentManager componentManager, int entityId, StatusEffectSource source)
    {
        EnsurePools(componentManager);

        if (TryResolveHazard(entityId, source, out var preferredType) && _bodyParts!.Has(entityId))
        {
            var partId = ResolveTargetPartId(entityId, preferredType);
            if (partId is { } resolvedPartId)
            {
                ApplyBodyPartScopedStack(entityId, resolvedPartId, source);
                return;
            }
        }

        BurningEffects.ApplyStack(componentManager, entityId, source);
    }

    private void EnsurePools(ComponentManager componentManager)
    {
        if (_poolsResolved)
        {
            return;
        }

        _entityTimers = componentManager.GetPackedPool<BurningTimerComponent>();
        _bodyParts = componentManager.IsRegistered<BodyPartComponent>() ? componentManager.GetMultiPool<BodyPartComponent>() : null;
        _bodyPartTimers = componentManager.GetMultiPool<BodyPartBurningTimerComponent>();
        _bodyPartStacks = componentManager.GetMultiPool<BodyPartStatusEffectStack>();
        _contactExposures = componentManager.IsRegistered<ContactDamageExposureComponent>() ? componentManager.GetPackedPool<ContactDamageExposureComponent>() : null;
        _hazards = componentManager.IsRegistered<DamageOnContactComponent>() ? componentManager.GetPackedPool<DamageOnContactComponent>() : null;
        _poolsResolved = true;
    }

    /// <summary>True if entityId's Burning grant should be body-part-scoped, out preferredType being the hazard's own BodyPartTargetRule.PreferredType (null means "no type preference, go straight to Bottommost").</summary>
    private bool TryResolveHazard(int entityId, StatusEffectSource source, out BodyPartType? preferredType)
    {
        if (source.Kind == StatusEffectSourceKind.Entity && _hazards?.TryGetReadonly(source.EntityId, out var sourceHazard) == true)
        {
            preferredType = sourceHazard.PreferredTargetType;
            return true;
        }

        if (_contactExposures?.TryGetReadonly(entityId, out var exposure) == true && _hazards?.TryGetReadonly(exposure.SourceEntityId, out var exposureHazard) == true)
        {
            preferredType = exposureHazard.PreferredTargetType;
            return true;
        }

        preferredType = null;
        return false;
    }

    /// <summary>Resolves which part a hazard-exposed entity's body-part-scoped burn should target.</summary>
    /// <remarks>
    /// Resolved with preferAlive: false -- deliberately *not* PickByTypeWithFallback's ordinary
    /// alive-preferring default -- so the same (preferredType, entity) pair always maps to the same
    /// part, whether or not that part is currently disabled. That stability is what keeps a single
    /// hazard's burn on the same part it started on for its whole lifetime, including after that
    /// part hits 0 and becomes disabled, rather than silently drifting to a different part (see
    /// BodyPartSelection.PickByTypeWithFallback's own doc comment for the full reasoning). It also
    /// means a *different* preferredType (e.g. a different hazard entered later, while an earlier
    /// hazard's burn on another part hasn't finished decaying yet) resolves independently to its own
    /// part rather than being folded into whatever's already burning -- entityId really can end up
    /// with several concurrently-burning parts this way, each from its own distinct hazard exposure,
    /// which is exactly what BodyPartBurningTimerComponent's own MultiComponentPool is shaped to
    /// support (see BodyPartBurningSystem's own doc comment).
    /// </remarks>
    private byte? ResolveTargetPartId(int entityId, BodyPartType? preferredType)
    {
        var rule = new BodyPartTargetRule(preferredType, BodyPartFallback.Bottommost);
        var denseIndex = BodyPartSelection.PickByTypeWithFallback(_bodyParts!, entityId, rule, mathUtility, preferAlive: false);
        return denseIndex == -1 ? null : _bodyParts!.GetReadonlyByDenseIndex(denseIndex).PartId;
    }

    /// <summary>Grants (or tops off) one Burning stack on entityId's partId -- mirrors BurningEffects.ApplyStack's own grant-or-top-off-capped-at-MaxStacks shape, scoped to the one part instead of the whole entity.</summary>
    private void ApplyBodyPartScopedStack(int entityId, byte partId, StatusEffectSource source)
    {
        var existingTimerDenseIndex = FindBodyPartTimer(entityId, partId);
        if (existingTimerDenseIndex != -1 && _bodyPartTimers!.GetReadonlyByDenseIndex(existingTimerDenseIndex).StackCount >= BurningEffects.MaxStacks)
        {
            return;
        }

        _bodyPartStacks!.Add(entityId, new BodyPartStatusEffectStack(partId, StatusEffectType.Burning, source));

        if (existingTimerDenseIndex != -1)
        {
            _bodyPartTimers!.UpdateByDenseIndex(existingTimerDenseIndex, static (ref BodyPartBurningTimerComponent t) => t.StackCount++);
        }
        else
        {
            _bodyPartTimers!.Add(entityId, new BodyPartBurningTimerComponent(partId, stackCount: 1, BurningEffects.TickIntervalFrames));
        }
    }

    private int FindBodyPartTimer(int entityId, byte partId)
    {
        for (var denseIndex = _bodyPartTimers!.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _bodyPartTimers.GetNextDenseIndex(denseIndex))
        {
            if (_bodyPartTimers.GetReadonlyByDenseIndex(denseIndex).PartId == partId)
            {
                return denseIndex;
            }
        }

        return -1;
    }
}
