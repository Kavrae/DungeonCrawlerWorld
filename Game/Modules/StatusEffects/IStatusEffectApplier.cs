using Game.World;

namespace Game.Modules.StatusEffects;

/// <summary>How one status effect's stacks are counted, added and removed on an entity, for anything that grants or removes that effect without knowing which effect it is.</summary>
/// <remarks>
/// Each effect module registers its own in StatusEffectApplierRegistry during IGameModule.Configure
/// (see BurningModule/PoisonModule). Callers: the effect entries StatusEffectGrant and
/// StatusEffectRemoval, whatever applied them.
/// </remarks>
public interface IStatusEffectApplier
{
    StatusEffectType EffectType { get; }

    /// <summary>The most stacks of EffectType an entity can hold; 1 for an effect that doesn't stack.</summary>
    int MaxStacks { get; }

    /// <summary>This entity's current stack count for EffectType, or 0 if it has none.</summary>
    /// <param name="bodyPartId">The one body part to count the stacks of, or null for the stacks the entity holds as a whole. An effect that isn't held per body part ignores it.</param>
    int GetCurrentStackCount(int entityId, byte? bodyPartId = null);

    /// <summary>Adds up to count stacks, attributed to source, and returns how many landed.</summary>
    /// <remarks>
    /// One immunity check and one write however many stacks are asked for. Fewer than count land
    /// when the entity reaches MaxStacks; none land on an immune entity. An effect that doesn't stack
    /// refreshes itself on an entity that already has it, and lands 0.
    /// </remarks>
    /// <param name="now">The simulation frame the stacks land on -- a newly started effect's timer is scheduled from it (FrameDeadline.After(now, ...)).</param>
    /// <param name="announcesRefusal">False to leave an immunity's blocked event unpublished: the caller already reported this refusal.</param>
    /// <param name="bodyPartId">The one body part to hold the stacks on, or null for the entity as a whole. Named by whatever grants the stacks, never worked out here. An effect that isn't held per body part ignores it.</param>
    int ApplyStacks(int entityId, int count, ActionSource source, long now, bool announcesRefusal = true, byte? bodyPartId = null);

    /// <summary>Removes every stack of EffectType from entityId, ending the effect; a no-op when it has none.</summary>
    void RemoveAllStacks(int entityId);
}
