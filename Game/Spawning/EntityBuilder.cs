using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Math;
using Engine.Utilities;
using Game.Blueprints;
using Game.Modules.Actions;
using Game.Modules.Class;
using Game.Modules.Class.Components;
using Game.Modules.Core.Components;
using Game.Modules.Mana;
using Game.Modules.Race.Components;

namespace Game.Spawning;

/// <summary>Builds a blueprint into an entity's components: a blueprint and a seed, into a creature.</summary>
/// <remarks>
/// Building is in two steps: the skeleton is what the entity needs to exist on the map before it is ever
/// simulated (its spawn record, and a NonBlockingComponent for a part that never blocks its cell), the body
/// is every definition in the blueprint's resolved build order -- each one's race or class, then its own
/// blueprint -- then its action-lock stagger. One reusable random sequence, reseeded per entity, is the
/// BlueprintContext.Rolls every part builds with, so an entity's rolls depend on its seed alone -- not on
/// how many entities were built before it, in what order, or when.
///
/// Needs no world, tiers or clock, which is what lets SpawnRecordRebuilder rebuild a record in a staging
/// world against the session's own definitions. EntityFactory builds through one of these.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EntityBuilder
{
    /// <summary>The longest a freshly built entity waits before its first action, so a batch built together doesn't act in lockstep.</summary>
    private static readonly ushort MaximumStaggerFrames = GameTiming.FramesForSeconds(1f);

    private readonly BlueprintRegistry _definitions;
    private readonly EntityKeys _entityKeys;
    private readonly SeededRandom _random = new();
    private readonly MathUtility _rolls;

    public EntityBuilder(BlueprintRegistry definitions, EntityKeys entityKeys)
    {
        _definitions = definitions;
        _entityKeys = entityKeys;
        _rolls = new MathUtility(_random);
    }

    /// <summary>The skeleton, then everything else (BuildComplete).</summary>
    /// <param name="now">The frame the body is built on -- the action-lock stagger counts from it.</param>
    public void Build(ComponentManager componentManager, int entityId, ushort blueprintId, uint seed, long now, SpawnFlags flags = SpawnFlags.None)
    {
        BuildSkeleton(componentManager, entityId, blueprintId, seed, flags);
        BuildComplete(componentManager, entityId, blueprintId, seed, now);
    }

    /// <summary>Writes entityId's skeleton (see EntityFactory.SkeletonComponentTypes) from its blueprint: an unplaced transform on the blueprint's layer and size if it has none yet, its occupancy and its spawn record.</summary>
    public void BuildSkeleton(ComponentManager componentManager, int entityId, ushort blueprintId, uint seed, SpawnFlags flags = SpawnFlags.None)
    {
        var resolved = _definitions.Resolve(blueprintId);
        if (!componentManager.GetDirectPool<TransformComponent>().Has(entityId))
        {
            componentManager.Merge(entityId, new TransformComponent(TransformComponent.UnplacedOn(resolved.Layer), resolved.Size));
        }

        foreach (var kind in resolved.NonBlocking)
        {
            componentManager.Merge(entityId, new NonBlockingComponent(kind));
        }

        componentManager.Merge(entityId, new SpawnRecordComponent(blueprintId, flags, seed));
    }

    /// <summary>Builds everything but the skeleton: each part in build order -- its race or class, then its Build step -- then its action-lock stagger.</summary>
    /// <param name="now">The frame it is built on -- the action-lock stagger counts from it.</param>
    public void BuildComplete(ComponentManager componentManager, int entityId, ushort blueprintId, uint seed, long now)
    {
        var resolved = _definitions.Resolve(blueprintId);
        _random.Reseed(seed);
        var context = new BlueprintContext(componentManager, entityId, _rolls, _entityKeys, seed, _definitions);

        foreach (var partId in resolved.BuildOrder)
        {
            BuildPart(context, partId);
        }

        GrantManaIfAnyActionCosts(componentManager, entityId, resolved.Actions);

        var actionLocks = componentManager.GetPackedPool<ActionLockComponent>();
        if (actionLocks.Has(entityId))
        {
            ActionLockGate.Lock(actionLocks, entityId, now, (ushort)_rolls.Next(0, MaximumStaggerFrames + 1));
        }
    }

    /// <summary>Builds blueprintId's parts onto a built entity, skipping any it already has, and records each part it builds as an AppliedBlueprintComponent.</summary>
    /// <remarks>See EntityFactory.Apply, which builds a skeleton before calling this.</remarks>
    public void Apply(ComponentManager componentManager, int entityId, ushort blueprintId, ClassGrantKind classGrantedBy)
    {
        var blueprint = _definitions.Resolve(blueprintId);
        var appliedParts = componentManager.GetMultiPool<AppliedBlueprintComponent>();

        ResolvedBlueprint? own = null;
        var seed = 0u;
        if (componentManager.GetDirectPool<SpawnRecordComponent>().TryGetReadonly(entityId, out var record))
        {
            seed = record.Seed;
            _definitions.TryResolve(record.BlueprintId, out own);
        }

        _random.Reseed(seed ^ ((ulong)(uint)blueprint.Definition.Id.GetHashCode() << 32));
        var context = new BlueprintContext(componentManager, entityId, _rolls, _entityKeys, seed, _definitions);

        foreach (var partId in blueprint.BuildOrder)
        {
            if (own?.BuildOrder.Contains(partId) == true || IsApplied(appliedParts, entityId, partId))
            {
                continue;
            }

            var definition = _definitions.Get(partId);
            if (definition.NonBlocking is { } kind)
            {
                componentManager.Merge(entityId, new NonBlockingComponent(kind));
            }

            BuildPart(context, partId, classGrantedBy);
            appliedParts.Add(entityId, new AppliedBlueprintComponent(partId, (ushort)appliedParts.CountForEntity(entityId)));
            GrantManaIfAnyActionCosts(componentManager, entityId, definition.Actions);
        }
    }

    private static bool IsApplied(MultiComponentPool<AppliedBlueprintComponent> appliedParts, int entityId, ushort partId)
    {
        for (var denseIndex = appliedParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = appliedParts.GetNextDenseIndex(denseIndex))
        {
            if (appliedParts.GetReadonlyByDenseIndex(denseIndex).BlueprintId == partId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Grants partId's race or class, then builds its own blueprint, so a blueprint already sees the race or class it belongs to.</summary>
    private void BuildPart(BlueprintContext context, ushort partId, ClassGrantKind classGrantedBy = ClassGrantKind.Spawn)
    {
        var definition = _definitions.Get(partId);
        var componentManager = context.ComponentManager;

        if (definition.Race is not null)
        {
            componentManager.Merge(context.EntityId, new RaceSlotsComponent(partId));
        }

        if (definition.Class is not null)
        {
            ClassEffects.Grant(componentManager, context.EntityId, partId, classGrantedBy);
        }

        definition.Build?.Invoke(context);
    }

    /// <summary>The definition-grant counterpart of ActionGrantEffects' mana hook: an entity whose blueprint grants a mana-costing action gains a ManaComponent, once every part has granted the ability scores it is sized from.</summary>
    private static void GrantManaIfAnyActionCosts(ComponentManager componentManager, int entityId, IReadOnlyList<ActionGrant> actions)
    {
        for (var index = 0; index < actions.Count; index++)
        {
            if (actions[index].ManaCost > 0)
            {
                ManaGrant.EnsureManaComponentExists(componentManager, entityId);
                return;
            }
        }
    }
}
