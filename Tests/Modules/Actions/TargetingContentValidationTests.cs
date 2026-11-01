using Engine.Tiles;
using Engine.Modules;
using Game.Effects;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Tags;

namespace Tests.Modules.Actions;

/// <summary>A build refuses an activation that offers Target mode where there is nothing aimed at to mark (a melee swing, an Adjacent shape around its caster), a range band with nothing in it, and a targeting setting nothing reads.</summary>
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
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Build(Action("Lunge", new TargetingSpec(TargetShape.SingleTarget, MaximumRange: 1), GameTags.DeliveryMelee)));

        Assert.Contains("Action 'Lunge'", exception.Message);
        Assert.Contains("Delivery.Melee", exception.Message);
    }

    [TestMethod]
    public void AdjacentShapeOfferingTargetMode_Throws()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Build(Action("Sweep", new TargetingSpec(TargetShape.Adjacent, MaximumRange: 0, Anchor: TargetAnchor.Caster))));

        Assert.Contains("Adjacent", exception.Message);
    }

    [TestMethod]
    public void MeleeGroundOnly_Builds()
    {
        Build(Action("Jab", new TargetingSpec(TargetShape.Adjacent, MaximumRange: 0, Modes: TargetingModes.GroundOnly, Anchor: TargetAnchor.Caster), GameTags.DeliveryMelee));
    }

    [TestMethod]
    public void RangedOfferingBothModes_Builds()
    {
        Build(Action("Bolt", new TargetingSpec(TargetShape.SingleTarget, MaximumRange: 10), GameTags.DeliveryRanged));
    }

    [TestMethod]
    public void AdjacentAtThePathEndOfferingTargetMode_Builds()
    {
        Build(Action("Ring Splash", new TargetingSpec(TargetShape.Adjacent, MaximumRange: 5)));
    }

    [TestMethod]
    public void MinimumRangePastMaximumRange_Throws_NamingTheAction()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Build(Action("Mortar", new TargetingSpec(TargetShape.SingleTarget, MaximumRange: 3, MinimumRange: 4))));

        Assert.Contains("Action 'Mortar'", exception.Message);
        Assert.Contains("minimum range", exception.Message);
    }

    [TestMethod]
    public void LineOfSightRequired_Builds()
    {
        Build(Action("Fireball", new TargetingSpec(TargetShape.Circle, MaximumRange: 10, AreaSize: 3, AimLineOfSight: LineOfSightRule.Required, ShapeLineOfSight: LineOfSightRule.Required)));
    }

    [TestMethod]
    [DataRow(TargetPath.Projectile, ProjectilePathEnd.AtAimedTile, (ushort)0, PathWallRule.StopsAtWall, (byte)0)]
    [DataRow(TargetPath.Projectile, ProjectilePathEnd.AtMaximumRange, (ushort)1, PathWallRule.BouncesOffWalls, (byte)2)]
    [DataRow(TargetPath.Projectile, ProjectilePathEnd.AtAimedTile, (ushort)1, PathWallRule.PassesThroughWalls, (byte)0)]
    [DataRow(TargetPath.Lobbed, ProjectilePathEnd.AtAimedTile, (ushort)0, PathWallRule.BouncesOffWalls, (byte)2)]
    public void PathSettings_Build(TargetPath path, ProjectilePathEnd pathEnd, ushort hitCount, PathWallRule pathWalls, byte bounces)
    {
        var targeting = new TargetingSpec(TargetShape.SingleTarget, MaximumRange: 10, Path: path, PathEnd: pathEnd, ProjectileHitCount: hitCount,
            PathWalls: pathWalls, WallBounceCount: bounces);

        Build(Action("Bolt", targeting));
    }

    [TestMethod]
    [DataRow(TargetPath.Placed, ProjectilePathEnd.AtMaximumRange, (ushort)0, PathWallRule.StopsAtWall, (byte)0)]
    [DataRow(TargetPath.Placed, ProjectilePathEnd.AtAimedTile, (ushort)1, PathWallRule.StopsAtWall, (byte)0)]
    [DataRow(TargetPath.Placed, ProjectilePathEnd.AtAimedTile, (ushort)0, PathWallRule.PassesThroughWalls, (byte)0)]
    [DataRow(TargetPath.Lobbed, ProjectilePathEnd.AtMaximumRange, (ushort)0, PathWallRule.StopsAtWall, (byte)0)]
    [DataRow(TargetPath.Lobbed, ProjectilePathEnd.AtAimedTile, (ushort)1, PathWallRule.StopsAtWall, (byte)0)]
    [DataRow(TargetPath.Projectile, ProjectilePathEnd.AtAimedTile, (ushort)0, PathWallRule.StopsAtWall, (byte)2)]
    public void PathSettingsNothingReads_Throw_NamingTheAction(TargetPath path, ProjectilePathEnd pathEnd, ushort hitCount, PathWallRule pathWalls, byte bounces)
    {
        var targeting = new TargetingSpec(TargetShape.SingleTarget, MaximumRange: 10, Path: path, PathEnd: pathEnd, ProjectileHitCount: hitCount,
            PathWalls: pathWalls, WallBounceCount: bounces);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Build(Action("Bolt", targeting)));

        Assert.Contains("Action 'Bolt'", exception.Message);
    }

    [TestMethod]
    public void MaximumRangeOnAShapePlacedAroundItsCaster_Throws()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Build(Action("Fan", new TargetingSpec(TargetShape.Cone, MaximumRange: 4, AreaSize: 4, Anchor: TargetAnchor.Caster))));

        Assert.Contains("maximum range", exception.Message);
    }

    [TestMethod]
    public void AShapePlacedAroundItsCaster_IsAimedWithinItsArea()
    {
        var targeting = new TargetingSpec(TargetShape.Cone, MaximumRange: 0, AreaSize: 4, Anchor: TargetAnchor.Caster, MinimumRange: 4);

        Build(Action("Fan", targeting));
        Assert.AreEqual(4, targeting.AimRanges.MaximumRange);
    }

    [TestMethod]
    public void SelfWhereThePathEnds_Throws()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Build(Action("Splash", new TargetingSpec(TargetShape.Self, MaximumRange: 5))));

        Assert.Contains("SingleTarget", exception.Message);
    }

    [TestMethod]
    public void SingleTargetOnTheCaster_Throws()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Build(Action("Pat", new TargetingSpec(TargetShape.SingleTarget, MaximumRange: 0, Anchor: TargetAnchor.Caster))));

        Assert.Contains("Self", exception.Message);
    }

    [TestMethod]
    [DataRow(TargetShape.SingleTarget)]
    [DataRow(TargetShape.Adjacent)]
    [DataRow(TargetShape.Sweep)]
    public void AreaSizeOnAShapeWithNoSize_Throws(TargetShape shape)
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Build(Action("Poke", new TargetingSpec(shape, MaximumRange: 5, AreaSize: 2))));

        Assert.Contains("area size", exception.Message);
    }
}
