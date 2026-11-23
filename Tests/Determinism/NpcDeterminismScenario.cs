using Engine.Decisions;
using Engine.ECS.Components;
using Engine.ECS.Properties;
using Engine.Math;
using Engine.Modules;
using Game.Blueprints.Composites;
using Game.Blueprints.NPCs.Generic;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Effects;
using Game.Modules;
using Game.Modules.Burning;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Movement.Components;
using Game.Modules.NpcBehavior;
using Game.Modules.NpcBehavior.Components;
using Game.Modules.Poison;
using Game.Spawning;
using Game.Views;
using Game.World;
using Tests.GameTests.Modules.NpcBehavior;

namespace Tests.Determinism;

/// <summary>A whole built-in session of mixed creatures fighting, fleeing, healing, looting and wandering, with every built-in behavior recorded as the real system runs it.</summary>
internal sealed class NpcDeterminismScenario
{
    public const int DefaultSessionSeed = 7;
    public const int FramesBetweenSamples = 3;
    public const int SampleCount = 300;

    private const int GroundLayer = (int)MapLayer.Ground;

    private static readonly RandomStreamKey[] GameplayStreams =
        [EffectServices.RandomStream, BurningModule.TickRandomStream, PoisonModule.TickRandomStream, NpcBehaviorModule.BehaviorRandomStream];

    private readonly NpcDecisionSystemTests.Session _session;

    public NpcDeterminismScenario(int sessionSeed = DefaultSessionSeed)
    {
        var recordingModule = new BehaviorRecordingModule(this);
        var build = GameBuildPass.Run(
            GameBootstrapper.BuiltInModules(),
            BuiltInTestModules.Factories<GameModuleContext>([recordingModule]),
            new Map(new Vector3Int(48, 48, 3)),
            TestContent.Layers,
            sessionSeed,
            [],
            initialEntityCapacity: 400,
            initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(At(24, 24));
        var playerId = build.Factory.Spawn(SpawnRequest.At(build.Context.Definitions.GetId(Player.Id), At(46, 46)) with { Seed = 1, Size = new Vector2Byte(1, 1) });
        build.EcsContext.ComponentManager.GetPackedPool<MovementComponent>().Remove(playerId);
        build.World.PlayerEntityId = playerId;
        _session = new NpcDecisionSystemTests.Session(build, playerId);
        build.EcsContext.EventBus.Subscribe<ActivationUsedEvent>(used =>
            Trace.RecordActivation(build.Context.SimulationClock.CurrentFrame, build.Context.EntityKeys.GetKey(used.EntityId).Value, used.Kind, used.DefinitionId));
        SpawnCreatures();
    }

    public SimulationTrace Trace { get; } = new();

    /// <summary>Each behavior that has applied to some creature while choosing.</summary>
    public HashSet<Guid> BehaviorsThatApplied { get; } = [];

    /// <summary>Each behavior some creature has acted on.</summary>
    public HashSet<Guid> BehaviorsActedOn { get; } = [];

    /// <summary>Whether a behavior carrying a consideration has applied, so the consideration was asked.</summary>
    public bool AskedAConsideration { get; private set; }

    private ComponentManager Components => _session.Components;

    private GameModuleContext Context => _session.Build.Context;

    public void RunFrames(int count) => _session.RunFrames(count);

    /// <summary>What every creature free to decide right now would choose, by entity key, with every factor of the choice, each chosen in the given order.</summary>
    public Dictionary<ulong, string> ChooseForAll(bool inReverseOrder)
    {
        var services = new NpcDecisionServices(Context, Components, Context.RandomStreams.SeedFor(NpcBehaviorModule.BehaviorRandomStream));
        var arbiter = new BehaviorArbiter<NpcDecisionContext>(NpcBehaviorModule.ArbiterOptions);
        var trace = new DecisionTrace();
        var decisions = Components.GetPackedPool<NpcDecisionComponent>();
        var commitments = Components.GetPackedPool<BehaviorCommitment>();
        var transforms = Components.GetDirectPool<TransformComponent>();
        var movements = Components.GetPackedPool<MovementComponent>();
        var now = Context.SimulationClock.CurrentFrame;
        var view = _session.DecisionView;

        var deciding = new List<int>(decisions.Count);
        for (var denseIndex = 0; denseIndex < decisions.Count; denseIndex++)
        {
            var entityId = decisions.GetEntityIdByDenseIndex(denseIndex);
            if (view.GateOf(entityId).Kind == NpcDecisionGateKind.Free)
            {
                deciding.Add(entityId);
            }
        }

        deciding.Sort();
        if (inReverseOrder)
        {
            deciding.Reverse();
        }

        var choices = new Dictionary<ulong, string>(deciding.Count);
        foreach (var entityId in deciding)
        {
            var transform = transforms.GetReadonly(entityId);
            var set = Context.NpcDecisions.Sets.Get(decisions.GetReadonly(entityId).BehaviorSetId);
            var commitment = set.CanCommit && commitments.TryGetReadonly(entityId, out var held) ? held : default;
            var seed = NpcDecisionContext.SeedFor(services, entityId, now);
            var context = new NpcDecisionContext(services, entityId, now, in transform, movements.Has(entityId), seed, isActing: false);
            var choice = arbiter.Choose(ref context, set, commitment, trace);
            var findings = context.Findings;
            choices[Context.EntityKeys.GetKey(entityId).Value] =
                $"{choice} {trace.Path} [{string.Join("; ", trace.Entries)}] heal {findings.SelfHealActionId}/{findings.SelfHealStackInstanceId} loot {findings.LootTarget} retry {findings.RetryCorpsesLater} flee {findings.FleeFrom}/{findings.FleeStep}";
        }

        return choices;
    }

    /// <summary>Every instance version of every component of every entity on the map, which any write moves.</summary>
    public List<uint> EveryComponentVersion()
    {
        var versions = new List<uint>();
        var transforms = Components.GetDirectPool<TransformComponent>();
        var pools = Components.AllPools.OfType<IDescribedComponentPool>().OrderBy(static pool => pool.ComponentType.FullName, StringComparer.Ordinal).ToList();
        for (var entityId = 0; entityId < transforms.Capacity; entityId++)
        {
            if (!transforms.TryGetReadonly(entityId, out _))
            {
                continue;
            }

            foreach (var pool in pools)
            {
                pool.CopyInstanceVersions(entityId, versions);
            }
        }

        return versions;
    }

    /// <summary>Where every creature stands and how healthy it is, by entity key.</summary>
    public List<string> EndState()
    {
        var state = new List<string>();
        var transforms = Components.GetDirectPool<TransformComponent>();
        for (var entityId = 0; entityId < transforms.Capacity; entityId++)
        {
            if (transforms.TryGetReadonly(entityId, out var transform))
            {
                state.Add($"{Context.EntityKeys.GetKey(entityId).Value}: {transform.Position} {_session.HealthFractionOf(entityId):R}");
            }
        }

        state.Sort(StringComparer.Ordinal);
        return state;
    }

    /// <summary>The next value of each gameplay random stream, which moves if anything has drawn from it. Draws from them, so it is the last thing asked of a session.</summary>
    public List<int> NextValueOfEachGameplayStream() =>
        [.. GameplayStreams.Select(key => Context.RandomStreams.Get(key).Next(0, int.MaxValue))];

    private static Vector3Int At(int x, int y) => new(x, y, GroundLayer);

    private void SpawnCreatures()
    {
        foreach (var (x, y) in new[] { (10, 10), (11, 10), (10, 11), (11, 11), (12, 11) })
        {
            _session.Spawn(Goblin.Id, At(x, y));
        }

        _session.Spawn(Fairy.Id, At(12, 10));
        _session.Spawn(Fairy.Id, At(15, 10));
        _session.Spawn(Ghost.Id, At(13, 12));
        _session.Spawn(GoblinEngineer.Id, At(12, 12));
        _session.Spawn(TestDummyBlueprint.Id, At(9, 10));

        var healer = _session.Spawn(Goblin.Id, At(24, 10));
        InventoryActions.AddItem(Components, healer, BuiltInItemIds.HealthPotion, 2);
        _session.HurtBelowHalf(healer);

        foreach (var (x, y) in new[] { (24, 20), (30, 20), (36, 20) })
        {
            var fleeing = _session.Spawn(Goblin.Id, At(x, y));
            _session.HurtUntil(fleeing, 0.6f);
            _session.Spawn(Fairy.Id, At(x + 1, y));
        }

        foreach (var (x, y) in new[] { (34, 36), (38, 36) })
        {
            _session.Spawn(Goblin.Id, At(x, y));
            SpawnCorpse(At(x + 1, y));
        }

        _session.Spawn(Goblin.Id, At(40, 10));
        _session.Spawn(Fairy.Id, At(6, 40));
        _session.Spawn(Fairy.Id, At(42, 46));
    }

    private void SpawnCorpse(Vector3Int position)
    {
        var corpse = _session.Spawn(Goblin.Id, position);
        InventoryActions.AddItem(Components, corpse, BuiltInItemIds.HealthPotion, 1);
        var eventBus = _session.Build.EcsContext.EventBus;
        eventBus.Publish(new EntityDiedEvent(corpse, ActionSource.Admin));
        eventBus.DispatchBuffered<EntityDiedEvent>();
    }

    /// <summary>Replaces each built-in behavior by its Guid, as a mod can, with one that records what the real one does.</summary>
    private sealed class BehaviorRecordingModule(NpcDeterminismScenario scenario) : IGameModule
    {
        public Guid Id { get; } = new("5e1f0a00-4c0d-4b1e-9a55-0000000000d1");

        public IReadOnlyList<Guid> Requires { get; } = [NpcBehaviorModule.ModuleId];

        public IReadOnlyList<Guid> RunsAfter { get; } = [NpcBehaviorModule.ModuleId];

        public void RegisterComponents(ComponentRegistration registration)
        {
        }

        public void Configure(GameModuleContext context)
        {
            foreach (var behavior in context.NpcBehaviors.Behaviors.ToList())
            {
                context.NpcBehaviors.Register(new RecordingBehavior(behavior, scenario));
            }
        }

        public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
        {
        }
    }

    private sealed class RecordingBehavior(IBehavior<NpcDecisionContext> behavior, NpcDeterminismScenario scenario) : IBehavior<NpcDecisionContext>
    {
        public Guid Id => behavior.Id;

        public string Name => behavior.Name;

        public Engine.Tags.GameplayTagSet Tags => behavior.Tags;

        public BehaviorEvaluation Evaluate(ref NpcDecisionContext context, BehaviorGrant<NpcDecisionContext> grant)
        {
            var evaluation = behavior.Evaluate(ref context, grant);
            if (evaluation.Rank > 0 && evaluation.Weight > 0)
            {
                scenario.BehaviorsThatApplied.Add(Id);
                scenario.AskedAConsideration |= grant.Considerations is { Count: > 0 };
            }

            return evaluation;
        }

        public void Act(ref NpcDecisionContext context, BehaviorGrant<NpcDecisionContext> grant)
        {
            scenario.BehaviorsActedOn.Add(Id);
            scenario.Trace.RecordDecision(context.Now, context.Services.EntityKeys.GetKey(context.EntityId).Value, Id);
            behavior.Act(ref context, grant);
        }
    }
}
