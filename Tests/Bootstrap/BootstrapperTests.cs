using Engine.Bootstrap;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Modules;
using Engine.Settings;

namespace Tests.Bootstrap;

[TestClass]
public sealed class BootstrapperTests
{
    private static readonly Guid CoreId = new("7b0e1c55-0000-4000-8000-000000000001");
    private static readonly Guid MovementId = new("7b0e1c55-0000-4000-8000-000000000002");
    private static readonly Guid BehaviorId = new("7b0e1c55-0000-4000-8000-000000000003");
    private static readonly Guid AbsentId = new("7b0e1c55-0000-4000-8000-0000000000ff");

    private static readonly SettingKey<int> CoreCapacity = new(CoreId, "Capacity");

    private sealed record TestBuildContext(string Name);

    private abstract class LoggingModule(string name, Guid id, List<string> log) : IModule<TestBuildContext>
    {
        public string Name => name;
        public Guid Id => id;
        public IReadOnlyList<Guid> Requires { get; init; } = [];
        public IReadOnlyList<Guid> RunsAfter { get; init; } = [];
        public IReadOnlyList<Guid> RunsBefore { get; init; } = [];
        public void RegisterComponents(ComponentRegistration registration) => log.Add($"{name}:components");
        public void Configure(TestBuildContext context) => log.Add($"{name}:configure:{context.Name}");
        public void RegisterSystems(SystemRegistration<TestBuildContext> registration) => log.Add($"{name}:systems:{registration.Context.Name}");
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

        public void RegisterSystems(SystemRegistration<TestBuildContext> registration) => log.Add($"systems:{registration.Settings.Get(CoreCapacity)}");
    }

    private static EcsContext Build(IReadOnlyList<IModule<TestBuildContext>> modules, SettingValues? settings = null) =>
        EcsBuilder.Begin(modules, settings ?? SettingValues.None, 10, 10, new EventBus())
            .RegisterComponents()
            .Configure(new TestBuildContext("shared"))
            .RegisterSystems()
            .Complete();

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
    public void Build_ConfiguresEveryModule_AfterEveryComponentAndBeforeAnySystem()
    {
        var log = new List<string>();

        Build([new MovementTestModule(log), new CoreTestModule(log)]);

        CollectionAssert.AreEqual(
            new[] { "Movement:components", "Core:components", "Movement:configure:shared", "Core:configure:shared", "Movement:systems:shared", "Core:systems:shared" },
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
    public void Begin_MissingRequirement_ThrowsNamingTheModuleAndTheMissingId()
    {
        var log = new List<string>();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Build([new MovementTestModule(log) { Requires = [CoreId] }]));

        Assert.Contains("Movement", exception.Message);
        Assert.Contains(CoreId.ToString(), exception.Message);
        Assert.IsEmpty(log);
    }

    [TestMethod]
    public void Begin_CircularOrdering_ThrowsWithThePath()
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
    public void Begin_CircularOrderingThroughRunsBefore_NamesTheDeclaringModuleAndList()
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
    public void Begin_TwoModulesSharingANonEmptyId_Throws()
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
    public void Begin_DuplicateModuleType_Throws()
    {
        var log = new List<string>();

        Assert.ThrowsExactly<InvalidOperationException>(() => Build([new CoreTestModule(log), new CoreTestModule(log)]));
    }

    [TestMethod]
    public void Build_ReturnsUsableEcsContext()
    {
        var log = new List<string>();

        var ecsContext = Build([new CoreTestModule(log)]);

        var entityId = ecsContext.EntityManager.CreateEntity();
        Assert.AreEqual(0, entityId);
    }

    [TestMethod]
    public void EveryStage_CanBeAdvancedOnlyOnce()
    {
        var log = new List<string>();
        var sortedModules = EcsBuilder.Begin<TestBuildContext>([new CoreTestModule(log)], SettingValues.None, 10, 10, new EventBus());

        var registeredComponents = sortedModules.RegisterComponents();
        Assert.ThrowsExactly<InvalidOperationException>(() => sortedModules.RegisterComponents());

        var configuredModules = registeredComponents.Configure(new TestBuildContext("first"));
        Assert.ThrowsExactly<InvalidOperationException>(() => registeredComponents.Configure(new TestBuildContext("second")));

        var registeredSystems = configuredModules.RegisterSystems();
        Assert.ThrowsExactly<InvalidOperationException>(() => configuredModules.RegisterSystems());

        registeredSystems.Complete();
        Assert.ThrowsExactly<InvalidOperationException>(() => registeredSystems.Complete());

        CollectionAssert.AreEqual(new[] { "Core:components", "Core:configure:first", "Core:systems:first" }, log);
    }

    [TestMethod]
    public void ResolvedSettings_ReachRegisterComponentsAndRegisterSystems()
    {
        var log = new List<string>();
        IReadOnlyList<IModule<TestBuildContext>> modules = [new SettingReadingModule(log)];
        var resolution = SettingsCatalog.Declare(modules).Resolve([new CommandLineSettingsSource(["--setting=SettingReadingModule.Capacity=64"])]);

        Build(modules, resolution.Values);

        Assert.IsEmpty(resolution.Failures);
        CollectionAssert.AreEqual(new[] { "components:64", "systems:64" }, log);
    }
}
