using Engine.Math;
using Engine.Modules;
using Game.Effects;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Tags;

namespace Tests.Modules.Actions;

/// <summary>A build refuses an activation that offers Target mode where there is nothing aimed at to mark: a melee swing, an Adjacent shape.</summary>
[TestClass]
public sealed class TargetingContentValidationTests
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

    private static ActionDefinition Action(string name, TargetingSpec targeting, params Engine.Tags.GameplayTag[] tags) =>
        new(Guid.NewGuid(), name, null, "?", default, [.. tags], Effects: [Effect.None],
            Activator: new DirectAction(targeting, new ActionTiming(ActionTimingCategory.Immediate)));

    private static void Build(ActionDefinition action) =>
        BuiltInTestModules.BuildModules([new RegisteringModule(context => context.Actions.Register(action))]);

    [TestMethod]
    public void MeleeOfferingTargetMode_Throws_NamingTheAction()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Build(Action("Lunge", new TargetingSpec(TargetShape.SingleTarget, Range: 1), GameTags.DeliveryMelee)));

        Assert.Contains("Action 'Lunge'", exception.Message);
        Assert.Contains("Delivery.Melee", exception.Message);
    }

    [TestMethod]
    public void AdjacentShapeOfferingTargetMode_Throws()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Build(Action("Sweep", new TargetingSpec(TargetShape.Adjacent, Range: 0))));

        Assert.Contains("Adjacent", exception.Message);
    }

    [TestMethod]
    public void MeleeGroundOnly_Builds()
    {
        Build(Action("Jab", new TargetingSpec(TargetShape.Adjacent, Range: 0, Modes: TargetingModes.GroundOnly), GameTags.DeliveryMelee));
    }

    [TestMethod]
    public void RangedOfferingBothModes_Builds()
    {
        Build(Action("Bolt", new TargetingSpec(TargetShape.SingleTarget, Range: 10), GameTags.DeliveryRanged));
    }
}
