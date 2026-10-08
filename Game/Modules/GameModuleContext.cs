using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Engine.Settings;
using Engine.Tags;
using Engine.Utilities;
using Game.Blueprints;
using Game.Blueprints.Objects;
using Game.Effects;
using Game.Modules.Achievements;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Lootboxes;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Auras;
using Game.Modules.StatusEffects;
using Game.Spawning;
using Game.World;

namespace Game.Modules;

/// <summary>Everything a module's Configure and RegisterBehavior can reach, shared across every module in one build.</summary>
/// <remarks>
/// Built once every component is registered and before any module is configured, so every member is
/// complete from construction: the objects that read pools are given them here. That makes the context
/// depend on the pools of <see cref="FoundationModuleIds"/>, which every build therefore contains.
/// </remarks>
public sealed class GameModuleContext
{
    /// <summary>The modules whose pools the context itself is built from: transforms and occupancy (Core), processing tiers (ProcessingTier) and spawn records (Blueprints).</summary>
    public static IReadOnlyList<Guid> FoundationModuleIds { get; } = [CoreModule.ModuleId, ProcessingTierModule.ModuleId, BlueprintsModule.ModuleId];

    /// <summary>Mixed into the runtime spawn seed so the loot box opener's sequence differs from the factory's.</summary>
    private const ulong LootboxRollSalt = 0x4C6F6F74626F7853;

    /// <param name="world">The build's World, over the pools of componentManager.</param>
    /// <param name="crawlerNumbers">The session's crawler numbers; null for a build that spawns no crawlers.</param>
    /// <param name="runtimeSpawnSeed">Seeds the factory's own sequence for spawns that name no seed.</param>
    public GameModuleContext(
        World.World world,
        ComponentManager componentManager,
        EntityManager entityManager,
        EventBus eventBus,
        SettingValues settings,
        GameplayTagRegistry gameplayTags,
        MathUtility mathUtility,
        UniqueNumberAllocator? crawlerNumbers = null,
        ulong runtimeSpawnSeed = 0)
    {
        MapQuery = world;
        PlayerQuery = world;
        EntityMoveSync = new WorldEventSync(world);
        ComponentManager = componentManager;
        EntityManager = entityManager;
        EventBus = eventBus;
        Settings = settings;
        GameplayTags = gameplayTags;
        MathUtility = mathUtility;
        EntityKeys = entityManager.Keys;
        Terrain = world.Terrain;
        AuraField = new AuraField(world, Terrain, Auras, eventBus);

        var tiers = componentManager.GetDirectPool<ProcessingTierComponent>();
        var transforms = componentManager.GetDirectPool<TransformComponent>();

        ProcessingTierResolver = new ProcessingTierResolver(tiers, transforms, ProcessingTierEvents);

        // Every placement through World gets a correct tier without its caller having to ask -- the
        // catch-all behind ProcessingTierResolver.CreateEntityAt. See that class's own remarks.
        world.EntityPlaced += ProcessingTierResolver.EnsureTiered;

        Lootboxes = new LootboxCatalog(Items);
        Items.AddDefinitionSource(Lootboxes);
        LootboxOpener = new LootboxOpener(componentManager, Lootboxes, Items, runtimeSpawnSeed ^ LootboxRollSalt);

        SimulationScope = new SimulationScope(new ProcessingTierQuery(tiers).IsSimulated);
        FloatingTextFeed = new FloatingTextFeed(eventBus, tiers, transforms);
        EntityFactory = new EntityFactory(Definitions, Auras, Actions, world, entityManager, componentManager, MovedEntities, SimulationClock, ProcessingTierEvents, ProcessingTierResolver, crawlerNumbers, runtimeSpawnSeed);
        _effectServices = new Lazy<EffectServices>(() => EffectServices.For(componentManager, EntityKeys, eventBus, mathUtility, PlayerQuery, Definitions, StatusEffectAppliers, Auras, FloatingTextFeed,
            entityManager, position => EntityFactory.Spawn(SpawnRequest.At(Definitions.GetId(AuraAnchor.Id), position))));
        _toggles = new Lazy<Toggles>(() => new Toggles(componentManager.GetMultiPool<ActiveToggleComponent>(), EffectServices));
        _targetResolution = new Lazy<TargetResolution>(() => new TargetResolution(world, transforms, EntityKeys, EffectServices.DeadEntities, componentManager.GetMultiPool<NonBlockingComponent>(), EffectServices.AbilityScores));
    }

    private readonly Lazy<EffectServices> _effectServices;

    private readonly Lazy<Toggles> _toggles;

    private readonly Lazy<TargetResolution> _targetResolution;

    public IMapQuery MapQuery { get; }

    /// <summary>Who the player is, for everything that treats the player differently.</summary>
    public IPlayerQuery PlayerQuery { get; }

    /// <summary>Map-occupancy sync MovementModule wires into MovementSystem.</summary>
    public IEntityMoveSync EntityMoveSync { get; }

    public ComponentManager ComponentManager { get; }

    public EntityManager EntityManager { get; }

    public EventBus EventBus { get; }

    /// <summary>Every setting this build's modules declared, resolved.</summary>
    public SettingValues Settings { get; }

    /// <summary>Every gameplay tag this build's modules declared, and each one's display name.</summary>
    public GameplayTagRegistry GameplayTags { get; }

    public MathUtility MathUtility { get; }

    /// <summary>
    /// Filled during Configure by every effect module -- see StatusEffectApplierRegistry's own doc
    /// comment for why registering here (during Configure) rather than in RegisterBehavior is what makes
    /// ordering safe.
    /// </summary>
    public StatusEffectApplierRegistry StatusEffectAppliers { get; } = new();

    /// <summary>Every aura definition, filled during Configure by whichever module owns each aura -- same reasoning as StatusEffectAppliers above.</summary>
    public AuraCatalog Auras { get; } = new();

    /// <summary>Where every aura reaches, for the aura system to apply and the map to draw the glow from. Follows the map's terrain itself; AurasModule's system puts entity sources into it.</summary>
    public AuraField AuraField { get; }

    /// <summary>Filled during Configure -- same reasoning as StatusEffectAppliers above.</summary>
    public StatusEffectDisplayRegistry StatusEffectDisplays { get; } = new();

    /// <summary>Filled during Configure -- same reasoning as StatusEffectAppliers above.</summary>
    public ActionCatalog Actions { get; } = new();

    /// <summary>Filled during Configure -- same reasoning as Actions above; a mod could register its own achievements the same way a mod could register its own actions.</summary>
    public AchievementCatalog Achievements { get; } = new();

    /// <summary>Filled during Configure -- same reasoning as Actions/Achievements above; a mod could register its own items the same way.</summary>
    public ItemCatalog Items { get; } = new();

    /// <summary>Every loot box type, filled during Configure, and the item definition of each type and rarity once it has been granted.</summary>
    public LootboxCatalog Lootboxes { get; }

    /// <summary>Opens an entity's loot boxes, drawing from its own sequence seeded from the runtime spawn seed.</summary>
    public LootboxOpener LootboxOpener { get; }

    /// <summary>MovementSystem's confirmed moves this frame, shared with TerrainContactSystem/AuraSystem so they can react without a per-move EventBus dispatch -- see FrameEventBuffer's own doc comment.</summary>
    public FrameEventBuffer<EntityMovedEvent> MovedEntities { get; } = new();

    /// <summary>Every tier change, for any module to subscribe to -- see ProcessingTierEvents' own doc comment.</summary>
    public ProcessingTierEvents ProcessingTierEvents { get; } = new();

    /// <summary>The single place an entity's tier is decided and written -- see ProcessingTierResolver's own doc comment.</summary>
    /// <remarks>Shared here, rather than owned privately by ProcessingTierSystem, because the spawn sequence needs it before the first system update: it sets the reference position ahead of population and creates entities through it so they are born correctly tiered.</remarks>
    public ProcessingTierResolver ProcessingTierResolver { get; }

    /// <summary>The simulation's "now", for anything a module builds that reads a FrameDeadline outside a system's own Update.</summary>
    /// <remarks>GameBuildPass hands this same instance to SystemManager.Clock, which is what advances it.</remarks>
    public SimulationClock SimulationClock { get; } = new();

    /// <summary>Which entities are simulated, for every timer wheel a module builds: an entity is simulated while its processing tier is below ProcessingTierDivisors.SimulatedTierCount -- see SimulationScope.</summary>
    /// <remarks>
    /// An entity with no ProcessingTierComponent counts as simulated. That is the opposite of the
    /// stripe sets' fail-open-to-Beyond, deliberately: a stripe set visiting an untiered entity rarely
    /// costs only staleness, while a timer wheel skipping one would stop its timers outright, and
    /// the entities that go untiered (never placed on the map) are exactly the ones nothing would
    /// ever resume.
    /// </remarks>
    public SimulationScope SimulationScope { get; }

    /// <summary>Where anything that happens to an entity publishes the floating text shown above it.</summary>
    public FloatingTextFeed FloatingTextFeed { get; }

    /// <summary>The pools and services every effect entry works with, for anything that applies effects: an action, an item, a terrain contact, an aura.</summary>
    /// <remarks>
    /// Made on first use, from pools other modules register (health, stat modifiers, ability scores,
    /// mana, death, auras, actions), so a build without those modules can still be made as long as
    /// nothing in it applies effects. A module that reads this declares those modules in Requires.
    /// </remarks>
    public EffectServices EffectServices => _effectServices.Value;

    /// <summary>What resolves a windup Actions doesn't resolve itself (an item's), filled during RegisterBehavior by the feature that owns it.</summary>
    public WindupResolvers WindupResolvers { get; } = new();

    /// <summary>Switches an entity's toggles on and off, for every feature that owns a kind of toggle (an action, an item).</summary>
    /// <remarks>Made on first use, like EffectServices, which it applies effects through: a module that reads this lists EffectServices.RequiredModuleIds in Requires.</remarks>
    public Toggles Toggles => _toggles.Value;

    /// <summary>Turns a caster's TargetSelection into the tiles an activation lands on, for every activation, windup, NPC choice and preview.</summary>
    /// <remarks>Made on first use, like Toggles: a module that reads this lists EffectServices.RequiredModuleIds in Requires.</remarks>
    public TargetResolution TargetResolution => _targetResolution.Value;

    /// <summary>The stable key table every entity is issued into -- the EntityManager's own.</summary>
    public EntityKeys EntityKeys { get; }

    /// <summary>Every terrain definition, filled during Configure -- same reasoning as StatusEffectAppliers above. A mod registers its own terrain here, or replaces a built-in by registering its key.</summary>
    public Terrain.TerrainRegistry Terrain { get; }

    public BlueprintRegistry Definitions { get; } = new();

    /// <summary>The build's one spawn path -- for a system or action that spawns an entity or applies a blueprint to one at runtime.</summary>
    /// <remarks>Spawn only once the build is complete: blueprints are resolved after every module's Configure.</remarks>
    public EntityFactory EntityFactory { get; }
}
