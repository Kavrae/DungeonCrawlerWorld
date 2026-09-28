using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Core.Components;
using Game.World;

namespace Game.Spawning;

/// <summary>Creatures spawned as skeletons -- placed on the map but not yet built -- and building them the first time they are simulated or otherwise touched.</summary>
/// <remarks>
/// A skeleton holds only what it needs to exist: TransformComponent and ProcessingTierComponent (the
/// spawner's), its SpawnRecordComponent and, for a race that never blocks, NonBlockingComponent (see
/// EntityFactory.BuildSkeleton) -- a crawler gets its number when it is built. Everything else is built by
/// EnsureBuilt from the spawn record, so it comes out exactly as a creature built at spawn would.
/// Building is one-way: a built creature is never returned to a skeleton, it stays built until its
/// neighborhood unloads.
///
/// Membership is a bit per entity id, cleared by Forget when the entity is destroyed.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class CreatureSkeletons(EntityFactory builder, ComponentManager componentManager, SimulationClock clock)
{
    private readonly SpawnMoves _spawnMoves = builder.SpawnMoves ?? throw new ArgumentException("Skeletons need a factory that spawns, not one that only builds.", nameof(builder));
    private readonly DirectComponentPool<TransformComponent> _transforms = componentManager.GetDirectPool<TransformComponent>();
    private readonly DirectComponentPool<SpawnRecordComponent> _spawnRecords = componentManager.GetDirectPool<SpawnRecordComponent>();

    private ulong[] _bits = [];

    /// <summary>How many skeletons exist.</summary>
    public int Count { get; private set; }

    public bool IsSkeleton(int entityId)
    {
        var word = entityId >> 6;
        return (uint)word < (uint)_bits.Length && (_bits[word] & (1UL << entityId)) != 0;
    }

    /// <summary>Gives entityId -- already created and carrying its TransformComponent -- a creature skeleton of blueprintId and seed.</summary>
    public void Spawn(int entityId, ushort blueprintId, uint seed, SpawnFlags flags = SpawnFlags.None)
    {
        builder.BuildSkeleton(componentManager, entityId, blueprintId, seed, flags);
        Mark(entityId);
    }

    /// <summary>Builds entityId's body if it is a skeleton, returning whether it was one.</summary>
    /// <remarks>
    /// The skeleton's TransformComponent is left as it was: no build step writes a skeleton component (see
    /// EntityFactory.SkeletonComponentTypes). Its spawn -- a move from its cell to the same cell, what
    /// spawning records for a creature built at spawn -- is recorded when it is built (see SpawnMoves), so
    /// aura and contact-damage exposures are granted then rather than having accrued while it was frozen. A build usually happens mid-frame, in the tier
    /// transition drain, after this frame's moves have been read, so that is usually the next frame.
    /// </remarks>
    public bool EnsureBuilt(int entityId)
    {
        if (!IsSkeleton(entityId))
        {
            return false;
        }

        Unmark(entityId);

        var record = _spawnRecords.GetReadonly(entityId);
        var transform = _transforms.GetReadonly(entityId);

        builder.BuildComplete(componentManager, entityId, record.BlueprintId, record.Seed, clock.CurrentFrame);

        _spawnMoves.Record(new EntityMovedEvent(entityId, transform.Position, transform.Position, transform.Size));
        return true;
    }

    /// <summary>Drops entityId from the skeletons, without building it -- for an entity being destroyed.</summary>
    public void Forget(int entityId)
    {
        if (IsSkeleton(entityId))
        {
            Unmark(entityId);
        }
    }

    private void Mark(int entityId)
    {
        var word = entityId >> 6;
        if (word >= _bits.Length)
        {
            Array.Resize(ref _bits, System.Math.Max(word + 1, _bits.Length * 2));
        }

        if ((_bits[word] & (1UL << entityId)) == 0)
        {
            _bits[word] |= 1UL << entityId;
            Count++;
        }
    }

    private void Unmark(int entityId)
    {
        _bits[entityId >> 6] &= ~(1UL << entityId);
        Count--;
    }
}
