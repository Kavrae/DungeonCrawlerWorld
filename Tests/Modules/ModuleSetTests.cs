using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Modules;

namespace Tests.Modules;

[TestClass]
public sealed class ModuleSetTests
{
    private sealed class TestModule(Guid id) : IModule
    {
        public Guid Id { get; } = id;
        public void RegisterComponents(ComponentManager componentManager) { }
        public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager) { }
    }

    [TestMethod]
    public void Combine_ModWithFreshId_IsAddedAlongsideBuiltIns()
    {
        var builtIn = new TestModule(new Guid("00000000-0000-0000-0000-000000000001"));
        var mod = new TestModule(new Guid("00000000-0000-0000-0000-000000000002"));

        var combined = ModuleSet.Combine([builtIn], [mod]);

        Assert.HasCount(2, combined);
        CollectionAssert.Contains(combined.ToList(), builtIn);
        CollectionAssert.Contains(combined.ToList(), mod);
    }

    [TestMethod]
    public void Combine_ModWithMatchingId_ReplacesTheBuiltIn()
    {
        var sharedId = new Guid("00000000-0000-0000-0000-000000000001");
        var builtIn = new TestModule(sharedId);
        var mod = new TestModule(sharedId);

        var combined = ModuleSet.Combine([builtIn], [mod]);

        Assert.HasCount(1, combined);
        Assert.AreSame(mod, combined[0]);
    }

    [TestMethod]
    public void Combine_TwoModsWithUnsetIds_BothAdded_NeitherReplacesTheOther()
    {
        var modA = new TestModule(Guid.Empty);
        var modB = new TestModule(Guid.Empty);

        var combined = ModuleSet.Combine([], [modA, modB]);

        Assert.HasCount(2, combined);
        CollectionAssert.Contains(combined.ToList(), modA);
        CollectionAssert.Contains(combined.ToList(), modB);
    }

    [TestMethod]
    public void Combine_NoMods_ReturnsBuiltInsUnchanged()
    {
        var builtIn = new TestModule(new Guid("00000000-0000-0000-0000-000000000001"));

        var combined = ModuleSet.Combine([builtIn], []);

        Assert.HasCount(1, combined);
        Assert.AreSame(builtIn, combined[0]);
    }
}
