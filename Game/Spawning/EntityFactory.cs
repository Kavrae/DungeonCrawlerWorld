using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints;
using Game.Modules.Actions;
using Game.Modules.Auras;
using Game.Modules.Class.Components;
using Game.Modules.Core.Components;
using Game.Modules.Crawler.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.World;

namespace Game.Spawning;

/// <summary>The one way an entity comes into the world: a blueprint and a seed, built into an entity and placed on the map.</summary>
/// <remarks>
/// Every entity spawns through this -- a goblin rolled by population, a shop, the player -- so every
/// entity carries a SpawnRecordComponent and can be deferred, rebuilt or read back as "blueprint plus
/// a seed". Building itself is EntityBuilder's; this adds what only a session has: placing on the map,
/// being born tiered, skeletons for creatures born unsimulated, spawn moves and crawler numbers.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EntityFactory
{
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
    private readonly EntityBuilder _builder;
    private readonly World.World _world;
    private readonly EntityManager _entityManager;
    private readonly ComponentManager _componentManager;
    private readonly ProcessingTierResolver _tierResolver;
    private readonly SimulationClock _clock;
    private readonly UniqueNumberAllocator? _crawlerNumbers;
    private readonly MathUtility _runtimeSeeds;
    private readonly DirectComponentPool<ProcessingTierComponent> _tiers;
    private readonly DirectComponentPool<TransformComponent> _transforms;

    /// <summary>Builds and spawns, into world and the ECS its pools belong to.</summary>
    /// <param name="tierResolver">Every entity is created through it, born with its processing tier as its first component, so no tiered consumer ever has to migrate it.</param>
    /// <param name="clock">The simulation's "now", which a freshly built entity's action-lock stagger counts from.</param>
    /// <param name="processingTierEvents">The session's tier changes, so a skeleton promoted into a simulated tier is built before anything sees the change.</param>
    /// <param name="crawlerNumbers">The session's crawler numbers, which a crawler draws from when it is first built; null for a session that spawns no crawlers.</param>
    /// <param name="runtimeSeed">Seeds the sequence a request that names no seed draws its own from -- kept apart from every other random sequence, so a spawn at runtime shifts nothing else.</param>
    public EntityFactory(
        BlueprintRegistry definitions,
        AuraCatalog auras,
        ActionCatalog actions,
        World.World world,
        EntityManager entityManager,
        ComponentManager componentManager,
        FrameEventBuffer<EntityMovedEvent> movedEntities,
        SimulationClock clock,
        ProcessingTierEvents processingTierEvents,
        ProcessingTierResolver tierResolver,
        UniqueNumberAllocator? crawlerNumbers = null,
        ulong runtimeSeed = 0)
    {
        _definitions = definitions;
        _builder = new EntityBuilder(definitions, auras, actions, entityManager.Keys);
        _world = world;
        _entityManager = entityManager;
        _componentManager = componentManager;
        _tierResolver = tierResolver;
        _clock = clock;
        _crawlerNumbers = crawlerNumbers;
        _runtimeSeeds = new MathUtility(new SeededRandom(runtimeSeed));
        _transforms = componentManager.GetDirectPool<TransformComponent>();
        _tiers = componentManager.GetDirectPool<ProcessingTierComponent>();
        SpawnMoves = new SpawnMoves(movedEntities, entityManager.Keys);

        Skeletons = new CreatureSkeletons(this, SpawnMoves, componentManager, clock);
        entityManager.EntityDestroying += Skeletons.Forget;
        processingTierEvents.TierChanging += BuildIfPromotedToSimulated;
    }

    /// <summary>Where every spawn's move is recorded -- register it first in the frame (see SpawnMoves).</summary>
    public SpawnMoves SpawnMoves { get; }

    /// <summary>The skeletons an entity born into an unsimulated tier is spawned as, built only once simulated (see CreatureSkeletons).</summary>
    /// <remarks>Created by the factory itself, since CreatureSkeletons is built around it. Forgets an entity as it is destroyed, ahead of any destruction handler registered after the factory, and builds one promoted into a simulated tier.</remarks>
    public CreatureSkeletons Skeletons { get; }

    /// <summary>Spawns one entity of blueprint at (x, y), everything else defaulting as <see cref="SpawnRequest"/> describes.</summary>
    /// <inheritdoc cref="Spawn(in SpawnRequest)"/>
    public int Spawn(Guid blueprint, int x, int y) => Spawn(new SpawnRequest(_definitions.GetId(blueprint), x, y));

    /// <summary>Creates, builds (or defers) and places one entity, returning its id -- or <see cref="NoEntity"/> when it landed off the map and was destroyed again.</summary>
    /// <exception cref="InvalidOperationException">The blueprint can't be spawned on its own (see ResolvedBlueprint.IsSpawnable), or the request is a Crawler and this factory was given no crawler numbers. Nothing is created.</exception>
    public int Spawn(in SpawnRequest request)
    {
        var world = _world;
        var entityManager = _entityManager;
        var componentManager = _componentManager;
        var resolved = RequireSpawnable(request.BlueprintId);

        if (request.Crawler && _crawlerNumbers is null)
        {
            throw new InvalidOperationException("A Crawler spawn needs the session's crawler numbers, and this factory was given none.");
        }

        var position = new Vector3Int(request.X, request.Y, (int)(request.Layer ?? resolved.Layer));
        var size = request.Size ?? resolved.Size;
        var seed = request.Seed ?? _runtimeSeeds.NextSeed();
        var flags = request.Crawler && _crawlerNumbers is { IsExhausted: false } ? SpawnFlags.Crawler : SpawnFlags.None;
        var entityId = request.ReservedEntityId ?? _tierResolver.CreateEntityAt(entityManager, position);
        var deferred = CanDefer(entityId, resolved);

        if (deferred)
        {
            componentManager.Merge(entityId, new TransformComponent(TransformComponent.UnplacedOn((MapLayer)position.Z), size));
            Skeletons.Spawn(entityId, request.BlueprintId, seed, flags);
        }
        else
        {
            _builder.BuildSkeleton(componentManager, entityId, request.BlueprintId, seed, flags);
            BuildComplete(componentManager, entityId, request.BlueprintId, seed, _clock.CurrentFrame);
        }

        ref var transform = ref _transforms.Get(entityId);
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
            // AuraSystem's own one-time startup scatter (EnsureGrid) only registers
            // SOURCES into the grid, it never grants to occupants already standing in one's radius.
            // Recorded into the shared buffer AuraSystem/TerrainContactSystem actually
            // drain, not published on the bus -- this is bulk population-time placement (tens of
            // thousands of entities per floor), not the rare player-move frequency PlayerActivityLog
            // is built around.
            SpawnMoves.Record(new EntityMovedEvent(entityId, position, position, transform.Size));
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
        Skeletons.EnsureBuilt(entityId);
        _builder.Apply(_componentManager, entityId, blueprintId, classGrantedBy);
    }

    /// <inheritdoc cref="EntityBuilder.BuildSkeleton"/>
    public void BuildSkeleton(ComponentManager componentManager, int entityId, ushort blueprintId, uint seed, SpawnFlags flags = SpawnFlags.None) =>
        _builder.BuildSkeleton(componentManager, entityId, blueprintId, seed, flags);

    /// <summary>Builds everything but the skeleton (see EntityBuilder.BuildComplete), then, for a crawler, its crawler number.</summary>
    /// <param name="now">The frame it is built on -- the action-lock stagger counts from it.</param>
    public void BuildComplete(ComponentManager componentManager, int entityId, ushort blueprintId, uint seed, long now)
    {
        _builder.BuildComplete(componentManager, entityId, blueprintId, seed, now);
        AssignCrawlerNumber(componentManager, entityId);
    }

    /// <summary>Gives entityId the session's next crawler number if its spawn record makes it a crawler and it has none yet.</summary>
    /// <remarks>
    /// On first build, not at spawn: a crawler number is only ever read on an entity that acts or is
    /// inspected built, so an unbuilt crawler never draws a number it may never use. A session given no crawler
    /// numbers assigns none. A crawler built after
    /// the numbers ran out has its Crawler flag cleared and stays a plain NPC.
    /// </remarks>
    private void AssignCrawlerNumber(ComponentManager componentManager, int entityId)
    {
        var spawnRecords = componentManager.GetDirectPool<SpawnRecordComponent>();
        if (_crawlerNumbers is null
            || !spawnRecords.TryGetReadonly(entityId, out var record)
            || !record.Flags.HasFlag(SpawnFlags.Crawler)
            || componentManager.GetPackedPool<CrawlerComponent>().Has(entityId))
        {
            return;
        }

        if (_crawlerNumbers.TryAllocate(out var crawlerNumber))
        {
            componentManager.Merge(entityId, new CrawlerComponent(crawlerNumber));
        }
        else
        {
            spawnRecords.TrySet(entityId, record with { Flags = record.Flags & ~SpawnFlags.Crawler });
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
        !IsBornSimulated(entityId)
        && blueprint.Deferrable;

    /// <summary>Builds entityId if it is a skeleton and tier is simulated.</summary>
    /// <remarks>On TierChanging, before any TierChanged handler (tier stripe sets, the Local roster, resume and timer catch-up) sees the change.</remarks>
    private void BuildIfPromotedToSimulated(int entityId, ProcessingTierLevel tier)
    {
        if (ProcessingTierQuery.IsSimulatedTier(tier))
        {
            Skeletons.EnsureBuilt(entityId);
        }
    }

    /// <summary>Whether entityId was born into a simulated tier. An entity born without a tier (one created on a reserved id) counts as simulated.</summary>
    private bool IsBornSimulated(int entityId) =>
        !_tiers.TryGetReadonly(entityId, out var tier) || ProcessingTierQuery.IsSimulatedTier(tier.Tier);
}
