using Engine.Bootstrap;
using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Modules;

namespace Tests.Bootstrap;

[TestClass]
public sealed class BootstrapperTests
{
    [TestMethod]
    public void Build_RegistersAllComponentsBeforeAnySystems()
    {
        var log = new List<string>();
        var (core, movement) = BuildCoreAndDependentModules(log);

        // Order doesn't matter here since Movement depends on Core; pass Movement first
        // to also confirm Bootstrapper doesn't just trust caller-supplied ordering.
        Bootstrapper.Build([movement, core], 10, 10);

        var lastComponentsIndex = log.FindLastIndex(entry => entry.EndsWith(":components"));
        var firstSystemsIndex = log.FindIndex(entry => entry.EndsWith(":systems"));

        Assert.IsLessThan(firstSystemsIndex, lastComponentsIndex);
    }

    [TestMethod]
    public void Build_DependencyIsRegisteredBeforeDependent_InBothPhases()
    {
        var log = new List<string>();
        var (coreModule, movementModule) = BuildCoreAndDependentModules(log);

        Bootstrapper.Build([movementModule, coreModule], 10, 10);

        Assert.IsLessThan(log.IndexOf("Movement:components"), log.IndexOf("Core:components"));
        Assert.IsLessThan(log.IndexOf("Movement:systems"), log.IndexOf("Core:systems"));
    }

    private sealed class CoreTestModule(List<string> log) : IModule
    {
        public string Name => "Core";
        public void RegisterComponents(ComponentManager componentManager) => log.Add("Core:components");
        public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager) => log.Add("Core:systems");
    }

    private sealed class MovementTestModule(List<string> log) : IModule
    {
        public string Name => "Movement";
        public IReadOnlyList<Type> Dependencies => [typeof(CoreTestModule)];
        public void RegisterComponents(ComponentManager componentManager) => log.Add("Movement:components");
        public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager) => log.Add("Movement:systems");
    }

    private static (IModule Core, IModule Movement) BuildCoreAndDependentModules(List<string> log) =>
        (new CoreTestModule(log), new MovementTestModule(log));

    [TestMethod]
    public void Build_MissingDependency_Throws()
    {
        var log = new List<string>();
        var movement = new MovementTestModule(log);

        Assert.ThrowsExactly<InvalidOperationException>(() => Bootstrapper.Build([movement], 10, 10));
    }

    private sealed class CircularModuleA : IModule
    {
        public IReadOnlyList<Type> Dependencies => [typeof(CircularModuleB)];
        public void RegisterComponents(ComponentManager componentManager) { }
        public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager) { }
    }

    private sealed class CircularModuleB : IModule
    {
        public IReadOnlyList<Type> Dependencies => [typeof(CircularModuleA)];
        public void RegisterComponents(ComponentManager componentManager) { }
        public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager) { }
    }

    [TestMethod]
    public void Build_CircularDependency_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => Bootstrapper.Build([new CircularModuleA(), new CircularModuleB()], 10, 10));
    }

    [TestMethod]
    public void Build_DuplicateModuleType_Throws()
    {
        var log = new List<string>();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            Bootstrapper.Build([new CoreTestModule(log), new CoreTestModule(log)], 10, 10));
    }

    [TestMethod]
    public void Build_ReturnsUsableEcsContext()
    {
        var log = new List<string>();
        var core = new CoreTestModule(log);

        var world = Bootstrapper.Build([core], 10, 10);

        var entityId = world.EntityManager.CreateEntity();
        Assert.AreEqual(0, entityId);
    }
}
