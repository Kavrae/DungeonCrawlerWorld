using Engine.Decisions;
using Engine.Math;
using Engine.Modules;
using Engine.Tags;
using Game.Blueprints;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Actions.Definitions;
using Game.Modules.BodyPartEffects;
using Game.Modules.Core;
using Game.Modules.Currency;
using Game.Modules.Death;
using Game.Modules.Health;
using Game.Modules.Inventory;
using Game.Modules.ItemPiles;
using Game.Modules.Mana;
using Game.Modules.Movement;
using Game.Modules.NpcBehavior.Behaviors;
using Game.Modules.NpcBehavior.Components;
using Game.Modules.NpcBehavior.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Race;
using Game.Modules.StatModifiers;
using Game.Tags;

namespace Game.Modules.NpcBehavior;

/// <summary>What NPCs decide to do: the Behavior tags and domain, the built-in behaviors, and NpcDecisionSystem, which decides for every entity whose blueprint grants behaviors.</summary>
/// <remarks>
/// The engine's framework (Engine.Decisions) chooses; this module says when to ask and supplies the pieces. Behaviors are
/// registered in Configure, a mod's alongside; RegisterBehavior then freezes the catalog, resolves every blueprint's set
/// (so a grant naming no behavior fails the build), and has every build mark the entities that decide
/// (NpcDecisionBuildStep). Runs before Movement, so what an NPC decides is taken the same frame.
/// </remarks>
public sealed class NpcBehaviorModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000018");

    /// <summary>The stream every NPC decision's own sequence is derived from (NpcDecisionContext): which behavior, which attack, which mode, where to step.</summary>
    public static readonly RandomStreamKey BehaviorRandomStream = new(ModuleId, "Behavior");

    /// <summary>A candidate under this share of the best weight at the winning rank never wins a draw.</summary>
    public static readonly BehaviorArbiterOptions ArbiterOptions = new(CutoffFraction: 0.25f);

    /// <summary>Every built-in behavior.</summary>
    public static IReadOnlyList<IBehavior<NpcDecisionContext>> BuiltInBehaviors { get; } = [new SelfHeal(), new EngageMelee(), new RangedAttack(), new Flee(), new LootNearby(), new WanderStep(), new Idle()];

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [CoreModule.ModuleId, HealthModule.ModuleId, InventoryModule.ModuleId, ActionsModule.ModuleId, BodyPartEffectsModule.ModuleId, ManaModule.ModuleId, DeathModule.ModuleId, CurrencyModule.ModuleId, ItemPilesModule.ModuleId, MovementModule.ModuleId, ProcessingTierModule.ModuleId, RaceModule.ModuleId, BlueprintsModule.ModuleId, StatModifiersModule.ModuleId, CoreActionsModule.ModuleId, CoreItemsModule.ModuleId, .. EffectServices.RequiredModuleIds];

    public IReadOnlyList<Guid> RunsBefore { get; } = [MovementModule.ModuleId];

    /// <summary>Declares the Behavior root, its drives (Behavior.Drive open to other modules, so a mod can add one) and the Behavior domain.</summary>
    public void DeclareTags(GameplayTagDeclarations tags)
    {
        tags.DeclareRoot(NpcDrives.Behavior, "What an NPC's behavior is, and what scales it.");
        tags.Declare(NpcDrives.Drive, "What a behavior serves; scaling a drive scales every behavior serving it.", extensible: true);
        tags.Declare(NpcDrives.Aggression, "Fighting: engaging in melee, attacking from range.");
        tags.Declare(NpcDrives.Caution, "Self-preservation: healing, running away.");
        tags.DeclareDomain(GameTagDomains.Behavior, "What a behavior is and what a behavior modifier targets.", extensible: false, [NpcDrives.Behavior]);
    }

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterPackedPool<NpcDecisionComponent>(static (ref existing, incoming) => existing.BehaviorSetId = incoming.BehaviorSetId);
        componentManager.RegisterPackedPool<BehaviorCommitment>(static (ref existing, incoming) => existing = incoming, initialCapacity: 64);
        componentManager.RegisterPackedPool<CorpseLootRetryComponent>(static (ref existing, incoming) => existing = incoming, initialCapacity: 64);
    }

    public void Configure(GameModuleContext context)
    {
        foreach (var behavior in BuiltInBehaviors)
        {
            context.NpcBehaviors.Register(behavior);
        }
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var componentManager = registration.ComponentManager;
        var decisions = context.NpcDecisions;

        decisions.Behaviors.Freeze(registration.Tags, GameTagDomains.Behavior);
        decisions.Sets.ResolveAll();
        context.EntityFactory.AddBuildStep(new NpcDecisionBuildStep(decisions.Sets));

        var services = new NpcDecisionServices(context, componentManager, context.RandomStreams.SeedFor(BehaviorRandomStream));
        registration.SystemManager.Register(new NpcDecisionSystem(
            services,
            decisions,
            componentManager.GetPackedPool<BehaviorCommitment>(),
            ArbiterOptions,
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            context.ProcessingTierEvents));
    }
}
