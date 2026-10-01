using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Blueprints;
using Game.Modules.Burning.Components;
using Game.Modules.Health;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Burning;

/// <summary>Burning's IStatusEffectApplier: a burn held on the one body part it is told, or on the entity as a whole when it is told none (BurningEffects.ApplyStacks).</summary>
/// <remarks>
/// Which part, if any, is the caller's to say -- the effect entry that grants the stacks names it
/// (StatusEffectGrant.BodyPart) -- and nothing here works it out from where the entity stands or what
/// else is acting on it. An entity can hold several burning parts at once, each with its own stacks
/// and its own tick (BodyPartBurningTimerComponent is a Multi pool), beside an entity-wide burn. A
/// part named for an entity that has no body parts is ignored: the burn is the entity's.
/// </remarks>
public sealed class BurningApplier(ComponentManager componentManager, BlueprintRegistry creatures, EventBus eventBus, IPlayerQuery playerQuery) : IStatusEffectApplier
{
    private readonly PackedComponentPool<BurningTimerComponent> _entityTimers = componentManager.GetPackedPool<BurningTimerComponent>();
    private readonly EntityBodyParts _bodyParts = EntityBodyParts.For(componentManager, creatures);
    private readonly MultiComponentPool<BodyPartBurningTimerComponent> _bodyPartTimers = componentManager.GetMultiPool<BodyPartBurningTimerComponent>();

    public StatusEffectType EffectType => StatusEffectType.Burning;

    public int MaxStacks => BurningEffects.MaxStacks;

    public int GetCurrentStackCount(int entityId, byte? bodyPartId = null)
    {
        if (bodyPartId is { } partId && _bodyParts.Has(entityId))
        {
            var timerDenseIndex = FindBodyPartTimer(entityId, partId);
            return timerDenseIndex == -1 ? 0 : _bodyPartTimers.GetReadonlyByDenseIndex(timerDenseIndex).StackCount;
        }

        return _entityTimers.TryGetReadonly(entityId, out var timer) ? timer.StackCount : 0;
    }

    public int ApplyStacks(int entityId, int count, ActionSource source, long now, bool announcesRefusal = true, byte? bodyPartId = null) =>
        bodyPartId is { } partId && _bodyParts.Has(entityId)
            ? ApplyBodyPartScopedStacks(entityId, partId, count, source, now, announcesRefusal)
            : BurningEffects.ApplyStacks(componentManager, entityId, count, source, now, eventBus, playerQuery, announcesRefusal);

    /// <summary>Puts out every burn on entityId: the entity-scoped one and every body part's.</summary>
    public void RemoveAllStacks(int entityId)
    {
        _entityTimers.Remove(entityId);
        _bodyPartTimers.Remove(entityId);
    }

    /// <summary>Adds up to count Burning stacks to entityId's partId and returns how many landed -- BurningEffects.ApplyStacks' own shape (immunity, capped at MaxStacks), scoped to the one part instead of the whole entity.</summary>
    private int ApplyBodyPartScopedStacks(int entityId, byte partId, int count, ActionSource source, long now, bool announcesRefusal)
    {
        if (count <= 0 || StatusEffectImmunity.IsImmune(componentManager, entityId, StatusEffectType.Burning, source, eventBus, playerQuery, announcesRefusal))
        {
            return 0;
        }

        var existingTimerDenseIndex = FindBodyPartTimer(entityId, partId);
        var existingStackCount = existingTimerDenseIndex == -1 ? 0 : _bodyPartTimers.GetReadonlyByDenseIndex(existingTimerDenseIndex).StackCount;
        var stacksLanded = Math.Min(count, BurningEffects.MaxStacks - existingStackCount);
        if (stacksLanded <= 0)
        {
            return 0;
        }

        if (existingTimerDenseIndex != -1)
        {
            _bodyPartTimers.UpdateByDenseIndex(existingTimerDenseIndex, (byte)stacksLanded, static (ref BodyPartBurningTimerComponent t, byte added) => t.StackCount += added);
        }
        else
        {
            _bodyPartTimers.Add(entityId, new BodyPartBurningTimerComponent(partId, stackCount: (byte)stacksLanded, FrameDeadline.AfterStaggered(now, BurningEffects.TickIntervalFrames, entityId), source));
            BodyPartDamageEffects.ResetRegenLockout(_bodyParts, entityId, partId, now);
        }

        return stacksLanded;
    }

    private int FindBodyPartTimer(int entityId, byte partId)
    {
        for (var denseIndex = _bodyPartTimers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _bodyPartTimers.GetNextDenseIndex(denseIndex))
        {
            if (_bodyPartTimers.GetReadonlyByDenseIndex(denseIndex).PartId == partId)
            {
                return denseIndex;
            }
        }

        return -1;
    }
}
