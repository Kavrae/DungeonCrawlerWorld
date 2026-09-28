using Engine.Bootstrap;
using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Modules;

namespace Tests.Bootstrap;

[TestClass]
public sealed class BootstrapperTests
{
    private static readonly Guid CoreId = new("7b0e1c55-0000-4000-8000-000000000001");
    private static readonly Guid MovementId = new("7b0e1c55-0000-4000-8000-000000000002");
    private static readonly Guid BehaviorId = new("7b0e1c55-0000-4000-8000-000000000003");
    private static readonly Guid AbsentId = new("7b0e1c55-0000-4000-8000-0000000000ff");

    private abstract class LoggingModule(string name, Guid id, List<string> log) : IModule
    {
        public string Name => name;
        public Guid Id => id;
        public IReadOnlyList<Guid> Requires { get; init; } = [];
        public IReadOnlyList<Guid> RunsAfter { get; init; } = [];
        public IReadOnlyList<Guid> RunsBefore { get; init; } = [];
        public void RegisterComponents(ComponentManager componentManager) => log.Add($"{name}:components");
        public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager) => log.Add($"{name}:systems");
    }

    private sealed class CoreTestModule(List<string> log) : LoggingModule("Core", CoreId, log);

    private sealed class ReplacementCoreTestModule(List<string> log) : LoggingModule("ReplacementCore", CoreId, log);

    private sealed class MovementTestModule(List<string> log) : LoggingModule("Movement", MovementId, log);

    private sealed class BehaviorTestModule(List<string> log) : LoggingModule("Behavior", BehaviorId, log);

    private sealed class UnidentifiedTestModule(List<string> log) : LoggingModule("Unidentified", Guid.Empty, log);

    private sealed class OtherUnidentifiedTestModule(List<string> log) : LoggingModule("OtherUnidentified", Guid.Empty, log);

    private static List<string> SystemsOrder(List<string> log) =>
        log.Where(entry => entry.EndsWith(":systems")).Select(entry => entry[..entry.IndexOf(':')]).ToList();

    [TestMethod]
    public void Build_RegistersAllComponentsBeforeAnySystems()
    {
        var log = new List<string>();

        Bootstrapper.Build([new MovementTestModule(log) { RunsAfter = [CoreId] }, new CoreTestModule(log)], 10, 10, new EventBus());

        var lastComponentsIndex = log.FindLastIndex(entry => entry.EndsWith(":components"));
        var firstSystemsIndex = log.FindIndex(entry => entry.EndsWith(":systems"));
        Assert.IsLessThan(firstSystemsIndex, lastComponentsIndex);
    }

    [TestMethod]
    public void Build_Unconstrained_KeepsTheCallersOrder()
    {
        var log = new List<string>();

        Bootstrapper.Build([new MovementTestModule(log), new BehaviorTestModule(log), new CoreTestModule(log)], 10, 10, new EventBus());

        CollectionAssert.AreEqual(new[] { "Movement", "Behavior", "Core" }, SystemsOrder(log));
    }

    [TestMethod]
    public void Build_RunsAfter_PutsTheTargetFirst_InBothPhases()
    {
        var log = new List<string>();

        Bootstrapper.Build([new MovementTestModule(log) { RunsAfter = [CoreId] }, new CoreTestModule(log)], 10, 10, new EventBus());

        Assert.IsLessThan(log.IndexOf("Movement:components"), log.IndexOf("Core:components"));
        Assert.IsLessThan(log.IndexOf("Movement:systems"), log.IndexOf("Core:systems"));
    }

    [TestMethod]
    public void Build_RunsBefore_PutsTheTargetAfter()
    {
        var log = new List<string>();

        Bootstrapper.Build([new MovementTestModule(log), new CoreTestModule(log), new BehaviorTestModule(log) { RunsBefore = [MovementId] }], 10, 10, new EventBus());

        CollectionAssert.AreEqual(new[] { "Behavior", "Movement", "Core" }, SystemsOrder(log));
    }

    [TestMethod]
    public void Build_OrderingTargetNotInTheSet_IsIgnored()
    {
        var log = new List<string>();

        Bootstrapper.Build([new MovementTestModule(log) { RunsAfter = [AbsentId], RunsBefore = [AbsentId] }, new CoreTestModule(log)], 10, 10, new EventBus());

        CollectionAssert.AreEqual(new[] { "Movement", "Core" }, SystemsOrder(log));
    }

    [TestMethod]
    public void Build_RequiresAlone_ImposesNoOrder()
    {
        var log = new List<string>();

        Bootstrapper.Build([new MovementTestModule(log) { Requires = [CoreId] }, new CoreTestModule(log)], 10, 10, new EventBus());

        CollectionAssert.AreEqual(new[] { "Movement", "Core" }, SystemsOrder(log));
    }

    [TestMethod]
    public void Build_Requirement_IsSatisfiedByAReplacementOfAnotherTypeWithTheSameId()
    {
        var log = new List<string>();

        Bootstrapper.Build([new ReplacementCoreTestModule(log), new MovementTestModule(log) { Requires = [CoreId], RunsAfter = [CoreId] }], 10, 10, new EventBus());

        CollectionAssert.AreEqual(new[] { "ReplacementCore", "Movement" }, SystemsOrder(log));
    }

    [TestMethod]
    public void Build_MissingRequirement_ThrowsNamingTheModuleAndTheMissingId()
    {
        var log = new List<string>();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Bootstrapper.Build([new MovementTestModule(log) { Requires = [CoreId] }], 10, 10, new EventBus()));

        Assert.Contains("Movement", exception.Message);
        Assert.Contains(CoreId.ToString(), exception.Message);
        Assert.IsEmpty(log);
    }

    [TestMethod]
    public void Build_CircularOrdering_ThrowsWithThePath()
    {
        var log = new List<string>();
        IReadOnlyList<IModule> modules =
        [
            new CoreTestModule(log) { RunsAfter = [MovementId] },
            new MovementTestModule(log) { RunsAfter = [BehaviorId] },
            new BehaviorTestModule(log) { RunsAfter = [CoreId] },
        ];

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Bootstrapper.Build(modules, 10, 10, new EventBus()));

        Assert.Contains("Core runs after Movement (Core.RunsAfter), Movement runs after Behavior (Movement.RunsAfter), Behavior runs after Core (Behavior.RunsAfter)", exception.Message);
    }

    [TestMethod]
    public void Build_CircularOrderingThroughRunsBefore_NamesTheDeclaringModuleAndList()
    {
        var log = new List<string>();
        IReadOnlyList<IModule> modules =
        [
            new CoreTestModule(log) { RunsAfter = [MovementId], RunsBefore = [MovementId] },
            new MovementTestModule(log),
        ];

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Bootstrapper.Build(modules, 10, 10, new EventBus()));

        Assert.Contains("Core runs after Movement (Core.RunsAfter), Movement runs after Core (Core.RunsBefore)", exception.Message);
    }

    [TestMethod]
    public void Build_MutualRequirements_AreAllowed()
    {
        var log = new List<string>();

        Bootstrapper.Build([new CoreTestModule(log) { Requires = [MovementId] }, new MovementTestModule(log) { Requires = [CoreId] }], 10, 10, new EventBus());

        CollectionAssert.AreEqual(new[] { "Core", "Movement" }, SystemsOrder(log));
    }

    [TestMethod]
    public void Build_TwoModulesSharingANonEmptyId_Throws()
    {
        var log = new List<string>();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            Bootstrapper.Build([new CoreTestModule(log), new ReplacementCoreTestModule(log)], 10, 10, new EventBus()));
    }

    [TestMethod]
    public void Build_ModulesWithoutAnId_Coexist()
    {
        var log = new List<string>();

        Bootstrapper.Build([new UnidentifiedTestModule(log), new OtherUnidentifiedTestModule(log)], 10, 10, new EventBus());

        CollectionAssert.AreEqual(new[] { "Unidentified", "OtherUnidentified" }, SystemsOrder(log));
    }

    [TestMethod]
    public void Build_DuplicateModuleType_Throws()
    {
        var log = new List<string>();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            Bootstrapper.Build([new CoreTestModule(log), new CoreTestModule(log)], 10, 10, new EventBus()));
    }

    [TestMethod]
    public void Build_ReturnsUsableEcsContext()
    {
        var log = new List<string>();

        var world = Bootstrapper.Build([new CoreTestModule(log)], 10, 10, new EventBus());

        var entityId = world.EntityManager.CreateEntity();
        Assert.AreEqual(0, entityId);
    }
}
