using Engine.Modules;
using Engine.Tiles;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;

namespace Tests.Effects;

/// <summary>A build refuses content whose effect names a terrain key nothing registered.</summary>
[TestClass]
public sealed class TerrainReferenceValidationTests
{
    private sealed class RegisteringModule(Action<GameModuleContext> configure) : IGameModule
    {
        public void RegisterComponents(ComponentRegistration registration)
        {
        }

        public void Configure(GameModuleContext context) => configure(context);

        public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
        {
        }
    }

    [TestMethod]
    public void UnregisteredTerrainKey_Throws_NamingTheActionAndTheKey()
    {
        var action = new ActionDefinition(Guid.NewGuid(), "Quicksand", null, "?", default, [], Effects: [new Effect([new TerrainChange("test:quicksand")])],
            Activator: new DirectAction(new TargetingSpec(TargetShape.Circle, MaximumRange: 5, AreaSize: 1), new ActionTiming(ActionTimingCategory.Immediate)));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            BuiltInTestModules.BuildModules([new RegisteringModule(context => context.Actions.Register(action))]));

        Assert.Contains("Action 'Quicksand'", exception.Message);
        Assert.Contains("test:quicksand", exception.Message);
    }
}
