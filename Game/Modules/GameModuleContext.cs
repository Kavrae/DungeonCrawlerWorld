using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Achievements;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.Modules.ProcessingTier;
using Game.Modules.StatusEffects;
using Game.World;
using Game.Blueprints;

namespace Game.Modules;

/// <summary>
/// Everything a module's Configure step can reach, shared across every module in one build.
/// </summary>
public sealed record GameModuleContext(IMapQuery MapQuery, MathUtility MathUtility, EventBus EventBus)
{
    /// <summary>Who the player is, for everything that treats the player differently.</summary>
    public required IPlayerQuery PlayerQuery { get; init; }

    /// <summary>Mandatory map-occupancy sync MovementModule wires into MovementSystem; MovementModule.RegisterSystems throws if this is still null when it constructs MovementSystem.</summary>
    public IEntityMoveSync? EntityMoveSync { get; init; }

    /// <summary>
    /// Shared across every module's Configure call within one GameBootstrapper.Build (or one
    /// DryRunValidateMods trial) -- a fresh registry per GameModuleContext instance, so the
    /// dry-run trial's registrations never leak into the real build's. See
    /// StatusEffectAuraApplierRegistry's own doc comment for why registering here (during
    /// Configure) rather than in RegisterComponents/RegisterSystems is what makes ordering safe.
    /// </summary>
    public StatusEffectAuraApplierRegistry StatusEffectAuraAppliers { get; init; } = new();

    /// <summary>Shared across every module's Configure call within one build -- same reasoning as StatusEffectAuraAppliers above.</summary>
    public StatusEffectDisplayRegistry StatusEffectDisplays { get; init; } = new();

    /// <summary>Shared across every module's Configure call within one build -- same reasoning as StatusEffectAuraAppliers above.</summary>
    public ActionCatalog Actions { get; init; } = new();

    /// <summary>Shared across every module's Configure call within one build -- same reasoning as Actions above; a mod could register its own achievements the same way a mod could register its own actions.</summary>
    public AchievementCatalog Achievements { get; init; } = new();

    /// <summary>Shared across every module's Configure call within one build -- same reasoning as Actions/Achievements above; a mod could register its own items the same way.</summary>
    public ItemCatalog Items { get; init; } = new();

    /// <summary>
    /// MovementSystem's confirmed moves this frame, shared with ContactDamageSystem/
    /// StatusEffectAuraSystem so they can react without a per-move EventBus dispatch -- see
    /// FrameEventBuffer's own doc comment. Always a real instance (never null), the same
    /// always-safe-default reasoning as Actions/StatusEffectAuraAppliers above.
    /// </summary>
    public FrameEventBuffer<EntityMovedEvent> MovedEntities { get; init; } = new();

    /// <summary>
    /// Shared across every module's Configure call within one build -- same always-real-default
    /// reasoning as MovedEntities above. Any module can subscribe to TierChanged regardless of
    /// whether ProcessingTierModule has run its own Configure/RegisterSystems yet -- see
    /// ProcessingTierEvents' own doc comment.
    /// </summary>
    public ProcessingTierEvents ProcessingTierEvents { get; init; } = new();

    /// <summary>
    /// The live Local-tier membership set, for consumers that act on only the Local population
    /// rather than throttling their visit cadence by tier -- see LocalTierRoster's own doc
    /// comment for why that needs its own shape rather than reusing TieredEntityStripeSet. Wired
    /// to ProcessingTierEvents above (and to the same driving pool ProcessingTierSystem tiers) by
    /// ProcessingTierModule.RegisterSystems; until that runs it is simply empty, which reads as
    /// "nothing is Local yet" -- the same safe default an untiered entity already gets.
    /// </summary>
    public LocalTierRoster LocalTierRoster { get; init; } = new();

    /// <summary>
    /// The single place an entity's tier is decided and written -- see ProcessingTierResolver's own
    /// doc comment. Shared here, rather than owned privately by ProcessingTierSystem, because the
    /// spawn sequence needs it *before* the first system update: it sets the reference position
    /// ahead of population and creates entities through it so they are born correctly tiered.
    /// Wired by ProcessingTierModule.RegisterSystems.
    /// </summary>
    public ProcessingTierResolver ProcessingTierResolver { get; init; } = new();

    /// <summary>
    /// The simulation's "now", for anything a module builds that reads a FrameDeadline outside a
    /// system's own Update. Always a real instance, the same always-safe-default reasoning as
    /// MovedEntities. GameBootstrapper hands this same instance to SystemManager.Clock, which is
    /// what advances it.
    /// </summary>
    public SimulationClock SimulationClock { get; init; } = new();

    /// <summary>
    /// Which entities are simulated, for every timer wheel a module builds -- see SimulationScope.
    /// Always a real instance; GameBootstrapper supplies the policy (an entity's processing tier
    /// against SystemManager.SimulatedTierCount) once the pools it reads exist.
    /// </summary>
    public SimulationScope SimulationScope { get; init; } = new();

    /// <summary>Where anything that happens to an entity publishes the floating text shown above it.</summary>
    /// <remarks>Always a real instance; GameBootstrapper wires it to the pools it reads once they exist. A module may keep it from Configure, but nothing may publish through it before it is wired.</remarks>
    public FloatingTextFeed FloatingTextFeed { get; init; } = new();

    /// <summary>The stable key table every entity is issued into, created here so modules can hold it before the ECS exists.</summary>
    /// <remarks>GameBootstrapper hands this same instance to Bootstrapper.Build, which gives it to the EntityManager that issues and releases the keys.</remarks>
    public EntityKeys EntityKeys { get; init; } = new();

    /// <summary>Every terrain definition, filled during Configure -- same reasoning as StatusEffectAuraAppliers above. A mod registers its own terrain here, or replaces a built-in by registering its key.</summary>
    public Terrain.TerrainRegistry Terrain { get; init; } = new();

    public Blueprints.BlueprintRegistry Definitions { get; init; } = new();

    /// <summary>The session's one spawn path -- for a system or action that spawns an entity or applies a blueprint to one at runtime.</summary>
    /// <remarks>Set by GameBootstrapper once the ECS is built, which is after every module's Configure: keep the context and read this when a system runs, never during Configure. Null in a dry run or a staging world, which never spawn.</remarks>
    public Spawning.EntityFactory? EntityFactory { get; set; }
}
