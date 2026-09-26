using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Utilities;
using Game.Blueprints;
using Game.Modules.Actions;
using Game.Modules.Class;
using Game.Modules.Class.Components;
using Game.Modules.Core.Components;
using Game.Modules.Crawler.Components;
using Game.Modules.Mana;
using Game.Modules.Mana.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Race.Components;
using Game.World;

namespace Game.Spawning;

/// <summary>The one way an entity comes into the world: a blueprint and a seed, built into an entity and placed on the map.</summary>
/// <remarks>
/// Every entity spawns through this -- a goblin rolled by population, a shop, the player -- so every
/// entity carries a SpawnRecordComponent and can be deferred, rebuilt or read back as "blueprint plus
/// a seed". Building is in two steps: the skeleton is what the entity needs to exist on the map before
/// it is ever simulated (its spawn record, and a NonBlockingComponent for a part that never blocks its
/// cell), the body is every definition in the blueprint's resolved build order -- each one's race or
/// class, then its own blueprint -- then its action-lock stagger. One reusable random sequence,
/// reseeded per entity, is the BlueprintContext.Rolls every part builds with, so an entity's rolls
/// depend on its seed alone -- not on how many entities were built before it, in what order, or when.
///
/// The spawning half (Spawn) needs the world, its entity manager and the session's tier resolver,
/// skeletons, move buffer and crawler numbers; the building half needs none of them, which is what lets
/// SpawnRecordRebuilder rebuild a record in a staging world with the build-only constructor.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EntityFactory
{
    /// <summary>The longest a freshly built entity waits before its first action, so a batch built together doesn't act in lockstep.</summary>
    private static readonly ushort MaximumStaggerFrames = GameTiming.FramesForSeconds(1f);

    /// <summary>What Spawn returns when the entity could not be placed and was destroyed again.</summary>
    public const int NoEntity = -1;

    /// <summary>The component types that make up an entity's skeleton: what it holds from creation, built or not, and all an unbuilt creature holds.</summary>
    /// <remarks>
    /// A type belongs here only if its value is declared on the entity's definitions or set by the spawn
    /// itself, and something needs it on unbuilt entities. BuildSkeleton writes these, from definitions and
    /// never from build code, and no build step writes them (BlueprintSkeletonTests), so the two never
    /// overlap. SkeletonAccessGuard guards every other pool; startup reserves these for the whole window.
    /// </remarks>
    public static readonly IReadOnlySet<Type> SkeletonComponentTypes = new HashSet<Type>
    {
        typeof(TransformComponent),
        typeof(ProcessingTierComponent),
        typeof(SpawnRecordComponent),
        typeof(NonBlockingComponent),
        typeof(ForceBlockingComponent),
    };

    private readonly BlueprintRegistry _definitions;
    private readonly EntityKeys _entityKeys;
    private readonly SeededRandom _random = new();
    private readonly MathUtility _rolls;

    private readonly World.World? _world;
    private readonly EntityManager? _entityManager;
    private readonly ComponentManager? _componentManager;
    private readonly ProcessingTierResolver? _tierResolver;
    private readonly SimulationClock? _clock;
    private readonly UniqueNumberAllocator? _crawlerNumbers;
    private readonly MathUtility? _runtimeSeeds;
    private readonly DirectComponentPool<ProcessingTierComponent>? _tiers;
    private readonly DirectComponentPool<TransformComponent>? _transforms;

    /// <summary>Builds only -- for a staging world that never places anything (see SpawnRecordRebuilder).</summary>
    public EntityFactory(BlueprintRegistry definitions, EntityKeys entityKeys)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(entityKeys);

        _definitions = definitions;
        _entityKeys = entityKeys;
        _rolls = new MathUtility(_random);
    }

    /// <summary>Builds and spawns, into world and the ECS its pools belong to.</summary>
    /// <param name="tierResolver">When supplied, an entity is created through it and born with its processing tier as its first component, so no tiered consumer ever has to migrate it.</param>
    /// <param name="clock">The simulation's "now", which a freshly built entity's action-lock stagger counts from.</param>
    /// <param name="crawlerNumbers">The session's crawler numbers, which a crawler draws from when it is first built.</param>
    /// <param name="runtimeSeed">Seeds the sequence a request that names no seed draws its own from -- kept apart from every other random sequence, so a spawn at runtime shifts nothing else.</param>
    public EntityFactory(
        BlueprintRegistry definitions,
        World.World world,
        EntityManager entityManager,
        ComponentManager componentManager,
        FrameEventBuffer<EntityMovedEvent> movedEntities,
        ProcessingTierResolver? tierResolver = null,
        SimulationClock? clock = null,
        UniqueNumberAllocator? crawlerNumbers = null,
        ulong runtimeSeed = 0)
        : this(definitions, entityManager?.Keys!)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(entityManager);
        ArgumentNullException.ThrowIfNull(componentManager);
        ArgumentNullException.ThrowIfNull(movedEntities);

        _world = world;
        _entityManager = entityManager;
        _componentManager = componentManager;
        SpawnMoves = new SpawnMoves(movedEntities, entityManager.Keys);
        _tierResolver = tierResolver;
        _clock = clock;
        _crawlerNumbers = crawlerNumbers;
        _runtimeSeeds = new MathUtility(new SeededRandom(runtimeSeed));
        _transforms = componentManager.GetDirectPool<TransformComponent>();
        _tiers = componentManager.IsRegistered<ProcessingTierComponent>() ? componentManager.GetDirectPool<ProcessingTierComponent>() : null;
    }

    /// <summary>Where every spawn's move is recorded -- register it first in the frame (see SpawnMoves). Null for a factory that only builds.</summary>
    public SpawnMoves? SpawnMoves { get; }

    /// <summary>When set, an entity born into an unsimulated tier is spawned as a skeleton and built only once simulated (see CreatureSkeletons); otherwise every entity is built at spawn.</summary>
    /// <remarks>Set after construction rather than taken as a parameter: CreatureSkeletons is built around this same factory, so one of the two has to be wired to the other afterwards.</remarks>
    public CreatureSkeletons? Skeletons { get; set; }

    /// <summary>Spawns one entity of blueprint at (x, y), everything else defaulting as <see cref="SpawnRequest"/> describes.</summary>
    /// <inheritdoc cref="Spawn(in SpawnRequest)"/>
    public int Spawn(Guid blueprint, int x, int y) => Spawn(new SpawnRequest(_definitions.GetId(blueprint), x, y));

    /// <summary>Creates, builds (or defers) and places one entity, returning its id -- or <see cref="NoEntity"/> when it landed off the map and was destroyed again.</summary>
    /// <exception cref="InvalidOperationException">The blueprint can't be spawned on its own (see ResolvedBlueprint.IsSpawnable), or the request is a Crawler and this factory was given no crawler numbers. Nothing is created.</exception>
    public int Spawn(in SpawnRequest request)
    {
        var world = _world ?? throw new InvalidOperationException("This factory builds only -- it was not given a world to spawn into.");
        var entityManager = _entityManager!;
        var componentManager = _componentManager!;
        var resolved = RequireSpawnable(request.BlueprintId);

        if (request.Crawler && _crawlerNumbers is null)
        {
            throw new InvalidOperationException("A Crawler spawn needs the session's crawler numbers, and this factory was given none.");
        }

        var position = new Vector3Int(request.X, request.Y, (int)(request.Layer ?? resolved.Layer));
        var size = request.Size ?? resolved.Size;
        var seed = request.Seed ?? _runtimeSeeds!.NextSeed();
        var flags = request.Crawler ? SpawnFlags.Crawler : SpawnFlags.None;
        var entityId = request.ReservedEntityId ?? _tierResolver?.CreateEntityAt(entityManager, position) ?? entityManager.CreateEntity();
        var deferred = CanDefer(entityId, resolved);

        if (deferred)
        {
            componentManager.Merge(entityId, new TransformComponent(TransformComponent.UnplacedOn((MapLayer)position.Z), size));
            Skeletons!.Spawn(entityId, request.BlueprintId, seed, flags);
        }
        else
        {
            Build(componentManager, entityId, request.BlueprintId, seed, _clock?.CurrentFrame ?? 0, flags);
        }

        ref var transform = ref _transforms!.Get(entityId);
        transform.Size = size;

        world.PlaceEntityOnMap(entityId, position, ref transform);

        if (!world.IsOnMap(transform.Position))
        {
            entityManager.DestroyEntity(entityId);
            return NoEntity;
        }

        // A skeleton records its spawn when it is built (CreatureSkeletons.EnsureBuilt): exposures don't accrue while frozen.
        if (!deferred)
        {
            // Spawning counts as a move (see EntityMovedEvent's own doc comment) so an entity placed
            // directly into a static aura source's range (e.g. spawned beside Lava) is granted
            // immediately, the same as one that later steps into range under its own power --
            // World.PlaceEntityOnMap itself never raises an EntityMovedEvent, and
            // StatusEffectAuraSystem's own one-time startup scatter (EnsureGrid) only registers
            // SOURCES into the grid, it never grants to occupants already standing in one's radius.
            // Recorded into the shared buffer StatusEffectAuraSystem/ContactDamageSystem actually
            // drain, not published on the bus -- this is bulk population-time placement (tens of
            // thousands of entities per floor), not the rare player-move frequency PlayerActivityLog
            // is built around.
            SpawnMoves!.Record(new EntityMovedEvent(entityId, position, position, transform.Size));
        }

        return entityId;
    }

    /// <summary>Builds blueprintId onto an entity that already exists -- a class it gains, a curse, a form it takes.</summary>
    /// <remarks>
    /// <para>
    /// Builds a skeleton first, since this is a gameplay write. Then builds the blueprint's parts in
    /// order, skipping any the entity already has -- one its own blueprint built, or one applied before --
    /// and records each part it builds as an AppliedBlueprintComponent. So applying anything twice, or
    /// Engineer to an engineer, changes nothing, and a merge policy never merges a part's writes into
    /// themselves.
    /// </para>
    /// <para>
    /// An applied part takes effect the way it can on a live entity: its race or class is granted (a
    /// class as classGrantedBy, since it did not come with the entity), its occupancy is added and its
    /// Build step runs. Its definition data is read through the applied list: its actions (see
    /// EntityActions), and its class name and name suffix (see EntityNaming). The rest of its appearance
    /// does not change the entity's, and the spawn record is untouched -- it stays what the entity was
    /// spawned as.
    /// </para>
    /// <para>
    /// Rolls come from the entity's own seed mixed with the blueprint's Guid, so applying the same
    /// blueprint to the same entity rolls the same way every time.
    /// </para>
    /// </remarks>
    public void Apply(int entityId, ushort blueprintId, ClassGrantKind classGrantedBy = ClassGrantKind.Spawn)
    {
        var componentManager = _componentManager ?? throw new InvalidOperationException("Cannot apply blueprint without a component manager.");
        var blueprint = _definitions.Resolve(blueprintId);
        var appliedParts = componentManager.GetMultiPool<AppliedBlueprintComponent>();

        Skeletons?.EnsureBuilt(entityId);

        ResolvedBlueprint? own = null;
        var seed = 0u;
        if (componentManager.IsRegistered<SpawnRecordComponent>() && componentManager.GetDirectPool<SpawnRecordComponent>().TryGetReadonly(entityId, out var record))
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

    /// <summary>The skeleton, then everything else (BuildComplete).</summary>
    /// <param name="now">The frame the body is built on -- the action-lock stagger counts from it.</param>
    public void Build(ComponentManager componentManager, int entityId, ushort blueprintId, uint seed, long now, SpawnFlags flags = SpawnFlags.None)
    {
        BuildSkeleton(componentManager, entityId, blueprintId, seed, flags);
        BuildComplete(componentManager, entityId, blueprintId, seed, now);
    }

    /// <summary>Writes entityId's skeleton (see SkeletonComponentTypes) from its blueprint: an unplaced transform on the blueprint's layer and size if it has none yet, its occupancy and its spawn record.</summary>
    public void BuildSkeleton(ComponentManager componentManager, int entityId, ushort blueprintId, uint seed, SpawnFlags flags = SpawnFlags.None)
    {
        ArgumentNullException.ThrowIfNull(componentManager);

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

    /// <summary>Builds everything but the skeleton: each part in build order -- its race or class, then its Build step -- then its action-lock stagger and, for a crawler, its crawler number.</summary>
    /// <param name="now">The frame it is built on -- the action-lock stagger counts from it.</param>
    public void BuildComplete(ComponentManager componentManager, int entityId, ushort blueprintId, uint seed, long now)
    {
        ArgumentNullException.ThrowIfNull(componentManager);

        var resolved = _definitions.Resolve(blueprintId);
        _random.Reseed(seed);
        var context = new BlueprintContext(componentManager, entityId, _rolls, _entityKeys, seed, _definitions);

        foreach (var partId in resolved.BuildOrder)
        {
            BuildPart(context, partId);
        }

        GrantManaIfAnyActionCosts(componentManager, entityId, resolved.Actions);

        if (componentManager.IsRegistered<ActionLockComponent>())
        {
            var actionLocks = componentManager.GetPackedPool<ActionLockComponent>();
            if (actionLocks.Has(entityId))
            {
                ActionLockGate.Lock(actionLocks, entityId, now, (ushort)_rolls.Next(0, MaximumStaggerFrames + 1));
            }
        }

        AssignCrawlerNumber(componentManager, entityId);
    }

    /// <summary>Gives entityId the session's next crawler number if its spawn record makes it a crawler and it has none yet.</summary>
    /// <remarks>
    /// On first build, not at spawn: a crawler number is only ever read on an entity that acts or is
    /// inspected built, so an unbuilt crawler never draws a number it may never use. A build-only factory
    /// (SpawnRecordRebuilder' staging world) has no crawler numbers and assigns none.
    /// </remarks>
    private void AssignCrawlerNumber(ComponentManager componentManager, int entityId)
    {
        if (_crawlerNumbers is not null
            && componentManager.GetDirectPool<SpawnRecordComponent>().TryGetReadonly(entityId, out var record)
            && record.Flags.HasFlag(SpawnFlags.Crawler)
            && componentManager.IsRegistered<CrawlerComponent>()
            && !componentManager.GetPackedPool<CrawlerComponent>().Has(entityId))
        {
            componentManager.Merge(entityId, new CrawlerComponent(_crawlerNumbers.Allocate()));
        }
    }

    /// <summary>Refuses a blueprint that can only be part of something else: an entity must be able to draw and name itself.</summary>
    private ResolvedBlueprint RequireSpawnable(ushort blueprintId)
    {
        var resolved = _definitions.Resolve(blueprintId);
        if (!resolved.IsSpawnable)
        {
            throw new InvalidOperationException(
                $"Blueprint '{resolved.Definition.Name}' can't be spawned on its own: its appearance has no {string.Join(", ", resolved.Appearance.Missing)}. Spawn a blueprint that includes it and supplies them.");
        }

        return resolved;
    }

    /// <summary>Whether entityId can be left unbuilt: born unsimulated, of a blueprint a skeleton can draw and name itself from (see ResolvedBlueprint.Deferrable).</summary>
    private bool CanDefer(int entityId, ResolvedBlueprint blueprint) =>
        Skeletons is not null
        && !IsBornSimulated(entityId)
        && blueprint.Deferrable;

    /// <summary>Whether entityId was born into a simulated tier. An entity born without a tier (no resolver) counts as simulated.</summary>
    private bool IsBornSimulated(int entityId) =>
        _tiers is null || !_tiers.TryGetReadonly(entityId, out var tier) || ProcessingTierQuery.IsSimulatedTier(tier.Tier);

    /// <summary>Grants partId's race or class, then builds its own blueprint, so a blueprint already sees the race or class it belongs to.</summary>
    private void BuildPart(BlueprintContext context, ushort partId, ClassGrantKind classGrantedBy = ClassGrantKind.Spawn)
    {
        var definition = _definitions.Get(partId);
        var componentManager = context.ComponentManager;

        if (definition.Race is not null && componentManager.IsRegistered<RaceSlotsComponent>())
        {
            componentManager.Merge(context.EntityId, new RaceSlotsComponent(partId));
        }

        if (definition.Class is not null && componentManager.IsRegistered<ClassSlotsComponent>())
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
            if (actions[index].ManaCost > 0 && componentManager.IsRegistered<ManaComponent>())
            {
                ManaGrant.EnsureManaComponentExists(componentManager, entityId);
                return;
            }
        }
    }
}
