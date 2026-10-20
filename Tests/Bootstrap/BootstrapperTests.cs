using Engine.Bootstrap;
using Engine.ECS.Systems;
using Engine.Modules;
using Engine.Settings;
using Game;

namespace Tests.Bootstrap;

[TestClass]
public sealed class BootstrapperTests
{
    private static readonly Guid CoreId = new("7b0e1c55-0000-4000-8000-000000000001");
    private static readonly Guid MovementId = new("7b0e1c55-0000-4000-8000-000000000002");
    private static readonly Guid BehaviorId = new("7b0e1c55-0000-4000-8000-000000000003");
    private static readonly Guid AbsentId = new("7b0e1c55-0000-4000-8000-0000000000ff");

    private static readonly SettingKey<int> CoreCapacity = new(CoreId, "Capacity");

    private sealed record TestBuildContext(string Name, SimulationClock Clock, Engine.ECS.Entities.EntityManager EntityManager);

    private abstract class LoggingModule(string name, Guid id, List<string> log) : IModule<TestBuildContext>
    {
        public string Name => name;
        public Guid Id => id;
        public IReadOnlyList<Guid> Requires { get; init; } = [];
        public IReadOnlyList<Guid> RunsAfter { get; init; } = [];
        public IReadOnlyList<Guid> RunsBefore { get; init; } = [];
        public void DeclareTags(Engine.Tags.GameplayTagDeclarations tags) => log.Add($"{name}:declare");
        public void RegisterComponents(ComponentRegistration registration) => log.Add($"{name}:components");
        public void Configure(TestBuildContext context) => log.Add($"{name}:configure:{context.Name}");
        public void RegisterBehavior(BehaviorRegistration<TestBuildContext> registration) => log.Add($"{name}:systems:{registration.Context.Name}");
    }

    private sealed class CoreTestModule(List<string> log) : LoggingModule("Core", CoreId, log);

    private sealed class ReplacementCoreTestModule(List<string> log) : LoggingModule("ReplacementCore", CoreId, log);

    private sealed class MovementTestModule(List<string> log) : LoggingModule("Movement", MovementId, log);

    private sealed class BehaviorTestModule(List<string> log) : LoggingModule("Behavior", BehaviorId, log);

    private sealed class UnidentifiedTestModule(List<string> log) : LoggingModule("Unidentified", Guid.Empty, log);

    private sealed class OtherUnidentifiedTestModule(List<string> log) : LoggingModule("OtherUnidentified", Guid.Empty, log);

    private sealed class SettingReadingModule(List<string> log) : IModule<TestBuildContext>
    {
        public Guid Id => CoreId;

        public void DeclareSettings(SettingsDeclarations settings) => settings.Declare(CoreCapacity, defaultValue: 16);

        public void RegisterComponents(ComponentRegistration registration) => log.Add($"components:{registration.Settings.Get(CoreCapacity)}");

        public void RegisterBehavior(BehaviorRegistration<TestBuildContext> registration) => log.Add($"systems:{registration.Settings.Get(CoreCapacity)}");
    }

    private struct ContractTestComponent;

    private sealed class PoolRegisteringModule(string name, Action<ComponentRegistration> registerComponents) : IModule<TestBuildContext>
    {
        public string Name => name;

        public Guid Id => CoreId;

        public void RegisterComponents(ComponentRegistration registration) => registerComponents(registration);

        public void RegisterBehavior(BehaviorRegistration<TestBuildContext> registration)
        {
        }
    }

    private static PoolRegisteringModule PackedPoolModule(string name) =>
        new(name, registration => registration.ComponentManager.RegisterPackedPool<ContractTestComponent>(static (ref existing, incoming) => existing = incoming));

    private static EcsBuild<TestBuildContext> Build(
        IReadOnlyList<IModule<TestBuildContext>> modules,
        IReadOnlyList<ISettingsSource>? settingsSources = null,
        IReadOnlyList<Guid>? requiredModuleIds = null,
        int simulatedTierCount = int.MaxValue,
        List<string>? log = null,
        IReadOnlyList<IModule<TestBuildContext>>? mods = null) =>
        EcsBuilder.Build(new EcsBuildRequest<TestBuildContext>
        {
            Modules = BuiltInTestModules.Factories(modules),
            Mods = BuiltInTestModules.Factories(mods ?? []),
            SettingsSources = settingsSources ?? [],
            FramesPerSecond = GameFrameRate.FramesPerSecond,
            InitialEntityCapacity = 10,
            InitialComponentCapacity = 10,
            RequiredModuleIds = requiredModuleIds ?? [],
            SimulatedTierCount = simulatedTierCount,
            CreateContext = services =>
            {
                log?.Add("CreateContext");
                return new TestBuildContext("shared", services.Clock, services.EntityManager);
            },
            AfterConfigure = _ => log?.Add("AfterConfigure"),
            AfterBehavior = (_, _) => log?.Add("AfterBehavior"),
        });

    private static List<string> SystemsOrder(List<string> log) =>
        log.Where(entry => entry.Contains(":systems")).Select(entry => entry[..entry.IndexOf(':')]).ToList();

    [TestMethod]
    public void Build_RegistersAllComponentsBeforeAnySystems()
    {
        var log = new List<string>();

        Build([new MovementTestModule(log) { RunsAfter = [CoreId] }, new CoreTestModule(log)]);

        var lastComponentsIndex = log.FindLastIndex(entry => entry.EndsWith(":components"));
        var firstSystemsIndex = log.FindIndex(entry => entry.Contains(":systems"));
        Assert.IsLessThan(firstSystemsIndex, lastComponentsIndex);
    }

    [TestMethod]
    public void Build_RunsEveryPhaseForEveryModule_BeforeTheNextPhase()
    {
        var log = new List<string>();

        Build([new MovementTestModule(log), new CoreTestModule(log)]);

        CollectionAssert.AreEqual(
            new[] { "Movement:declare", "Core:declare", "Movement:components", "Core:components", "Movement:configure:shared", "Core:configure:shared", "Movement:systems:shared", "Core:systems:shared" },
            log);
    }

    [TestMethod]
    public void Build_CallsTheGamesCallbacksBetweenThePhases()
    {
        var log = new List<string>();

        Build([new MovementTestModule(log), new CoreTestModule(log)], log: log);

        CollectionAssert.AreEqual(
            new[]
            {
                "Movement:declare", "Core:declare", "Movement:components", "Core:components", "CreateContext",
                "Movement:configure:shared", "Core:configure:shared", "AfterConfigure", "Movement:systems:shared", "Core:systems:shared", "AfterBehavior",
            },
            log);
    }

    [TestMethod]
    public void Build_Unconstrained_KeepsTheCallersOrder()
    {
        var log = new List<string>();

        Build([new MovementTestModule(log), new BehaviorTestModule(log), new CoreTestModule(log)]);

        CollectionAssert.AreEqual(new[] { "Movement", "Behavior", "Core" }, SystemsOrder(log));
    }

    [TestMethod]
    public void Build_RunsAfter_PutsTheTargetFirst_InEveryPhase()
    {
        var log = new List<string>();

        Build([new MovementTestModule(log) { RunsAfter = [CoreId] }, new CoreTestModule(log)]);

        Assert.IsLessThan(log.IndexOf("Movement:declare"), log.IndexOf("Core:declare"));
        Assert.IsLessThan(log.IndexOf("Movement:components"), log.IndexOf("Core:components"));
        Assert.IsLessThan(log.IndexOf("Movement:configure:shared"), log.IndexOf("Core:configure:shared"));
        Assert.IsLessThan(log.IndexOf("Movement:systems:shared"), log.IndexOf("Core:systems:shared"));
    }

    [TestMethod]
    public void Build_RunsBefore_PutsTheTargetAfter()
    {
        var log = new List<string>();

        Build([new MovementTestModule(log), new CoreTestModule(log), new BehaviorTestModule(log) { RunsBefore = [MovementId] }]);

        CollectionAssert.AreEqual(new[] { "Behavior", "Movement", "Core" }, SystemsOrder(log));
    }

    [TestMethod]
    public void Build_OrderingTargetNotInTheSet_IsIgnored()
    {
        var log = new List<string>();

        Build([new MovementTestModule(log) { RunsAfter = [AbsentId], RunsBefore = [AbsentId] }, new CoreTestModule(log)]);

        CollectionAssert.AreEqual(new[] { "Movement", "Core" }, SystemsOrder(log));
    }

    [TestMethod]
    public void Build_RequiresAlone_ImposesNoOrder()
    {
        var log = new List<string>();

        Build([new MovementTestModule(log) { Requires = [CoreId] }, new CoreTestModule(log)]);

        CollectionAssert.AreEqual(new[] { "Movement", "Core" }, SystemsOrder(log));
    }

    [TestMethod]
    public void Build_Requirement_IsSatisfiedByAReplacementOfAnotherTypeWithTheSameId()
    {
        var log = new List<string>();

        Build([new ReplacementCoreTestModule(log), new MovementTestModule(log) { Requires = [CoreId], RunsAfter = [CoreId] }]);

        CollectionAssert.AreEqual(new[] { "ReplacementCore", "Movement" }, SystemsOrder(log));
    }

    [TestMethod]
    public void Build_MissingRequirement_ThrowsNamingTheModuleAndTheMissingId_BeforeAnyModuleRuns()
    {
        var log = new List<string>();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Build([new MovementTestModule(log) { Requires = [CoreId] }]));

        Assert.Contains("Movement", exception.Message);
        Assert.Contains(CoreId.ToString(), exception.Message);
        Assert.IsEmpty(log);
    }

    [TestMethod]
    public void Build_MissingBuildRequirement_ThrowsNamingTheMissingId_BeforeAnyModuleRuns()
    {
        var log = new List<string>();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Build([new MovementTestModule(log)], requiredModuleIds: [CoreId]));

        Assert.Contains(CoreId.ToString(), exception.Message);
        Assert.IsEmpty(log);
    }

    [TestMethod]
    public void Build_CircularOrdering_ThrowsWithThePath()
    {
        var log = new List<string>();
        IReadOnlyList<IModule<TestBuildContext>> modules =
        [
            new CoreTestModule(log) { RunsAfter = [MovementId] },
            new MovementTestModule(log) { RunsAfter = [BehaviorId] },
            new BehaviorTestModule(log) { RunsAfter = [CoreId] },
        ];

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Build(modules));

        Assert.Contains("Core runs after Movement (Core.RunsAfter), Movement runs after Behavior (Movement.RunsAfter), Behavior runs after Core (Behavior.RunsAfter)", exception.Message);
    }

    [TestMethod]
    public void Build_CircularOrderingThroughRunsBefore_NamesTheDeclaringModuleAndList()
    {
        var log = new List<string>();
        IReadOnlyList<IModule<TestBuildContext>> modules =
        [
            new CoreTestModule(log) { RunsAfter = [MovementId], RunsBefore = [MovementId] },
            new MovementTestModule(log),
        ];

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Build(modules));

        Assert.Contains("Core runs after Movement (Core.RunsAfter), Movement runs after Core (Core.RunsBefore)", exception.Message);
    }

    [TestMethod]
    public void Build_MutualRequirements_AreAllowed()
    {
        var log = new List<string>();

        Build([new CoreTestModule(log) { Requires = [MovementId] }, new MovementTestModule(log) { Requires = [CoreId] }]);

        CollectionAssert.AreEqual(new[] { "Core", "Movement" }, SystemsOrder(log));
    }

    [TestMethod]
    public void Build_TwoModulesSharingANonEmptyId_Throws()
    {
        var log = new List<string>();

        Assert.ThrowsExactly<InvalidOperationException>(() => Build([new CoreTestModule(log), new ReplacementCoreTestModule(log)]));
    }

    [TestMethod]
    public void Build_ModulesWithoutAnId_Coexist()
    {
        var log = new List<string>();

        Build([new UnidentifiedTestModule(log), new OtherUnidentifiedTestModule(log)]);

        CollectionAssert.AreEqual(new[] { "Unidentified", "OtherUnidentified" }, SystemsOrder(log));
    }

    [TestMethod]
    public void Build_DuplicateModuleType_Throws()
    {
        var log = new List<string>();

        Assert.ThrowsExactly<InvalidOperationException>(() => Build([new CoreTestModule(log), new CoreTestModule(log)]));
    }

    [TestMethod]
    public void Build_ReturnsUsableEcsContext()
    {
        var log = new List<string>();

        var ecsContext = Build([new CoreTestModule(log)]).EcsContext;

        var entityId = ecsContext.EntityManager.CreateEntity();
        Assert.AreEqual(0, entityId);
    }

    [TestMethod]
    public void Build_TheContextIsGivenTheClockTheSystemManagerAdvances()
    {
        var log = new List<string>();

        var build = Build([new CoreTestModule(log)]);
        build.EcsContext.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, IsRunningSlowly: false, FrameCount: 9));

        Assert.AreSame(build.EcsContext.Clock, build.Context.Clock);
        Assert.AreEqual(9, build.Context.Clock.CurrentFrame);
    }

    [TestMethod]
    public void Build_AppliesTheRequestedSimulatedTierCount()
    {
        var log = new List<string>();

        var build = Build([new CoreTestModule(log)], simulatedTierCount: 2);

        Assert.AreEqual(2, build.EcsContext.SystemManager.SimulatedTierCount);
    }

    [TestMethod]
    public void ResolvedSettings_ReachRegisterComponentsAndRegisterBehavior()
    {
        var log = new List<string>();

        var build = Build([new SettingReadingModule(log)], settingsSources: [new CommandLineSettingsSource(["--setting=SettingReadingModule.Capacity=64"])]);

        Assert.IsEmpty(build.Declarations.SettingsFailures);
        CollectionAssert.AreEqual(new[] { "components:64", "systems:64" }, log);
    }

    [TestMethod]
    public void RejectedSettingOverrides_AreReportedOnTheBuild()
    {
        var log = new List<string>();

        var build = Build([new SettingReadingModule(log)], settingsSources: [new CommandLineSettingsSource(["--setting=Bogus.Name=1"])]);

        Assert.HasCount(1, build.Declarations.SettingsFailures);
    }

    private sealed class EntityCreatingModule(bool createsInConfigure) : IModule<TestBuildContext>
    {
        public void RegisterComponents(ComponentRegistration registration)
        {
        }

        public void Configure(TestBuildContext context)
        {
            if (createsInConfigure)
            {
                context.EntityManager.CreateEntity();
            }
        }

        public void RegisterBehavior(BehaviorRegistration<TestBuildContext> registration)
        {
            if (!createsInConfigure)
            {
                registration.Context.EntityManager.CreateEntity();
            }
        }
    }

    [TestMethod]
    public void Build_AnEntityCreatedInConfigure_Throws()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Build([new EntityCreatingModule(createsInConfigure: true)]));

        Assert.Contains("before the build completed", exception.Message);
    }

    [TestMethod]
    public void Build_AnEntityCreatedInRegisterBehavior_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => Build([new EntityCreatingModule(createsInConfigure: false)]));
    }

    [TestMethod]
    public void Build_ModReplacingAModule_WithTheSamePools_Builds()
    {
        var build = Build([PackedPoolModule("Core")], mods: [PackedPoolModule("ReplacementCore")]);

        Assert.AreEqual("ReplacementCore", build.Modules.Single().Name);
    }

    [TestMethod]
    public void Build_ModReplacingAModule_WithoutItsComponents_ThrowsNamingThem()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            Build([PackedPoolModule("Core")], mods: [new PoolRegisteringModule("ReplacementCore", _ => { })]));

        Assert.Contains("ReplacementCore replaces Core but does not register: ContractTestComponent (PackedComponentPool)", exception.Message);
    }

    [TestMethod]
    public void Build_ModReplacingAModule_WithAnotherKindOfPool_Throws()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            Build([PackedPoolModule("Core")], mods: [new PoolRegisteringModule("ReplacementCore", registration => registration.ComponentManager.RegisterMultiPool<ContractTestComponent>())]));

        Assert.Contains("ContractTestComponent (PackedComponentPool)", exception.Message);
    }

    [TestMethod]
    public void Declare_ReturnsWhatABuildOfTheSameSetWouldDeclare_WithoutRegisteringAnything()
    {
        var log = new List<string>();

        var declarations = EcsBuilder.Declare(BuiltInTestModules.Factories<TestBuildContext>([new SettingReadingModule(log)]), settingsSources: [new CommandLineSettingsSource(["--setting=SettingReadingModule.Capacity=64"])]);

        Assert.AreEqual(64, declarations.Settings.Get(CoreCapacity));
        Assert.IsEmpty(log);
    }
}
