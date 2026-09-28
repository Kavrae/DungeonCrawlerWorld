using Engine.Math;
using Engine.Modules;
using Game.Bootstrap;
using Game.Modules;
using Game.World;

namespace Tests.Bootstrap;

/// <summary>Every GameBuildPass is self-contained: nothing one pass creates is reachable from another.</summary>
[TestClass]
public sealed class GameBuildPassTests
{
    private sealed class RecordingModule : IGameModule
    {
        public GameModuleContext? ConfiguredWith { get; private set; }

        public GameModuleContext? SystemsRegisteredWith { get; private set; }

        public void RegisterComponents(ComponentRegistration registration)
        {
        }

        public void Configure(GameModuleContext context) => ConfiguredWith = context;

        public void RegisterSystems(SystemRegistration<GameModuleContext> registration) => SystemsRegisteredWith = registration.Context;
    }

    private static GameBuildPassResult Run(params ModuleFactory<GameModuleContext>[] mods) =>
        GameBuildPass.Run(GameBootstrapper.BuiltInModules(), mods, new Map(new Vector3Int(5, 5, 1)), new MathUtility(), [], initialEntityCapacity: 10, initialComponentCapacity: 10);

    [TestMethod]
    public void TwoPasses_ShareNoModuleInstance()
    {
        var first = Run();
        var second = Run();

        Assert.IsFalse(first.Modules.Any(module => second.Modules.Any(other => ReferenceEquals(module, other))));
    }

    [TestMethod]
    public void TwoPasses_ShareNoWorldContextEventBusOrEntityKeys()
    {
        var first = Run();
        var second = Run();

        Assert.AreNotSame(first.World, second.World);
        Assert.AreNotSame(first.Context, second.Context);
        Assert.AreNotSame(first.EcsContext.EventBus, second.EcsContext.EventBus);
        Assert.AreNotSame(first.EcsContext.EntityManager.Keys, second.EcsContext.EntityManager.Keys);
    }

    [TestMethod]
    public void APass_BuildsOverItsOwnWorld_WithTheContextItsModulesWereGiven()
    {
        var pass = Run(ModuleFactory<GameModuleContext>.For<RecordingModule>());

        var module = pass.Modules.OfType<RecordingModule>().Single();
        Assert.AreSame(pass.Context, module.ConfiguredWith);
        Assert.AreSame(pass.Context, module.SystemsRegisteredWith);
        Assert.AreSame(pass.World, pass.Context.MapQuery);
        Assert.AreSame(pass.World, pass.Context.PlayerQuery);
        Assert.AreSame(pass.EcsContext.EventBus, pass.Context.EventBus);
        Assert.AreSame(pass.Context.EntityKeys, pass.EcsContext.EntityManager.Keys);
    }

    [TestMethod]
    public void EachPass_ConfiguresAFreshInstanceOfTheSameMod_Once()
    {
        var modFactory = ModuleFactory<GameModuleContext>.For<RecordingModule>();

        var first = Run(modFactory).Modules.OfType<RecordingModule>().Single();
        var second = Run(modFactory).Modules.OfType<RecordingModule>().Single();

        Assert.AreNotSame(first, second);
        Assert.AreNotSame(first.ConfiguredWith, second.ConfiguredWith);
    }

    [TestMethod]
    public void AModuleBuildWithoutAFoundationModule_ThrowsNamingIt()
    {
        IReadOnlyList<IModule<GameModuleContext>> modules = [new Game.Modules.Core.CoreModule(), new Game.Blueprints.BlueprintsModule()];

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            GameBuildPass.BuildModules(modules, TestModuleRegistration.DefaultSettings(modules), new Map(new Vector3Int(5, 5, 1)), new MathUtility(), initialEntityCapacity: 10, initialComponentCapacity: 10));

        Assert.Contains(Game.Modules.ProcessingTier.ProcessingTierModule.ModuleId.ToString(), exception.Message);
    }

    [TestMethod]
    public void TwoSessionBuilds_ShareNoWorldEventBusOrDefinitions()
    {
        var first = GameBootstrapper.Build(ValidatedMods.None, new Map(new Vector3Int(5, 5, 1)), new MathUtility(), initialEntityCapacity: 10, initialComponentCapacity: 10);
        var second = GameBootstrapper.Build(ValidatedMods.None, new Map(new Vector3Int(5, 5, 1)), new MathUtility(), initialEntityCapacity: 10, initialComponentCapacity: 10);

        Assert.AreNotSame(first.World, second.World);
        Assert.AreNotSame(first.EcsContext.EventBus, second.EcsContext.EventBus);
        Assert.AreNotSame(first.Definitions, second.Definitions);
    }
}
