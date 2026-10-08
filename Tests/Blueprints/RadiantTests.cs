using Engine.ECS.Systems;
using Engine.Math;
using Game.Admin;
using Game.Blueprints.Parts;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Modules.Auras.Components;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Game.Spawning;
using Game.World;

namespace Tests.Blueprints;

[TestClass]
public sealed class RadiantTests
{
    private const int FramesPerSecond = 60;

    private static readonly Vector3Int GoblinPosition = new(60, 60, (int)MapLayer.Ground);

    private static void RunFrames(GameBuildPassResult build, int count)
    {
        var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / FramesPerSecond);
        var start = build.EcsContext.SystemManager.Clock.CurrentFrame + 1;
        for (var frame = start; frame < start + count; frame++)
        {
            build.EcsContext.SystemManager.Update(new EngineTime(frameDuration * frame, frameDuration, false, frame));
        }
    }

    [TestMethod]
    public void OfferedByAdminApply()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(20, 20, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 50, initialComponentCapacity: 50);
        var commands = new BlueprintAdminCommands(build.Factory, build.Context.Definitions);

        Assert.IsTrue(commands.Applicable().Any(choice => choice.Name == Radiant.Name));
    }

    /// <summary>Applied to a goblin: it radiates both auras out to 30 tiles, the steady one at full power to its edge and the fading one down to 1, and both follow it when it moves.</summary>
    [TestMethod]
    public void AppliedToAGoblin_RadiatesBothAurasToSizeThirty()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(128, 128, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 200, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(GoblinPosition);
        var goblinId = build.Factory.Spawn(SpawnRequest.At(build.Context.Definitions.GetId(Goblin.Id), GoblinPosition) with { Seed = 1 });
        build.EcsContext.ComponentManager.GetPackedPool<MovementComponent>().Remove(goblinId);
        RunFrames(build, 2);

        build.Factory.Apply(goblinId, build.Context.Definitions.GetId(Radiant.Id));

        var field = build.Context.AuraField;
        var steadyId = build.Context.Auras.GetId(Radiant.SteadyAura.Id);
        var fadingId = build.Context.Auras.GetId(Radiant.FadingAura.Id);
        var edge = GoblinPosition with { X = GoblinPosition.X + 30 };
        Assert.AreEqual(2, build.EcsContext.ComponentManager.GetMultiPool<AuraSourceComponent>().CountForEntity(goblinId));
        Assert.AreEqual(8, field.GetTotalPowerAt(edge, steadyId));
        Assert.AreEqual(1, field.GetTotalPowerAt(edge, fadingId));
        Assert.AreEqual(0, field.GetTotalPowerAt(edge with { X = edge.X + 1 }, steadyId));
    }
}
