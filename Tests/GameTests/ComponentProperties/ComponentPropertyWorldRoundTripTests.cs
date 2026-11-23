using Engine.ECS.Properties;
using Engine.ECS.Systems;
using Engine.Tiles;
using Game.Bootstrap;
using Game.Floors;
using Game.World;

namespace Tests.GameTests.ComponentProperties;

/// <summary>Every component of a populated, simulated world read through its table and each writable value written back unchanged through its pool.</summary>
[TestClass]
public sealed class ComponentPropertyWorldRoundTripTests
{
    private const int FramesPerSecond = 60;

    [TestMethod]
    public void EveryComponentOfABuiltWorld_ReadsAndTakesEveryWritableValueBackUnchanged()
    {
        var pass = BuildSimulatedWorld();
        var ecsContext = pass.EcsContext;
        var readInstances = 0;
        var writes = 0;
        var poolsHeld = 0;

        foreach (var pool in ecsContext.ComponentManager.PoolsInRegistrationOrder.Cast<IDescribedComponentPool>())
        {
            var poolHeld = false;
            for (var entityId = 0; entityId < ecsContext.EntityManager.Capacity; entityId++)
            {
                if (!ecsContext.EntityManager.EntityExists(entityId) || !pool.Has(entityId))
                {
                    continue;
                }

                poolHeld = true;
                var before = new RecordingVisitor();
                readInstances += pool.ReadProperties(entityId, before);
                var writesByInstance = new int[before.InstanceVersions.Count];
                foreach (var (instance, path, property, value, isWritable) in before.Leaves)
                {
                    var written = pool.WriteProperty(entityId, instance, path, value, out _);
                    if (written)
                    {
                        writesByInstance[instance]++;
                        writes++;
                    }
                    else
                    {
                        Assert.IsTrue(!isWritable || value.IsAbsent, $"{pool.ComponentType.Name} on {entityId} refused {string.Join('.', path)} ({property}) = {value}");
                    }
                }

                var after = new RecordingVisitor();
                pool.ReadProperties(entityId, after);
                Assert.HasCount(before.Leaves.Count, after.Leaves, pool.ComponentType.Name);
                for (var leaf = 0; leaf < before.Leaves.Count; leaf++)
                {
                    Assert.AreEqual(before.Leaves[leaf].Value.ToString(), after.Leaves[leaf].Value.ToString(), $"{pool.ComponentType.Name} on {entityId}: {before.Leaves[leaf].Property}");
                }

                for (var instance = 0; instance < writesByInstance.Length; instance++)
                {
                    Assert.AreEqual(
                        before.InstanceVersions[instance] + (uint)writesByInstance[instance],
                        after.InstanceVersions[instance],
                        $"{pool.ComponentType.Name} on {entityId}, instance {instance}: one version a write");
                }
            }

            poolsHeld += poolHeld ? 1 : 0;
        }

        Assert.IsGreaterThan(30, poolsHeld, "Precondition: most component types are held somewhere in the world.");
        Assert.IsGreaterThan(10_000, readInstances, "Precondition: a real population.");
        Assert.IsGreaterThan(10_000, writes, "Precondition: values were written back.");
    }

    [TestMethod]
    public void TheSameWriteRefused_LeavesTheVersionAlone()
    {
        var pass = BuildSimulatedWorld();
        var ecsContext = pass.EcsContext;
        var transforms = (IDescribedComponentPool)ecsContext.ComponentManager.GetDirectPool<Game.Modules.Core.Components.TransformComponent>();
        var player = pass.World.PlayerEntityId;
        var before = new RecordingVisitor();
        transforms.ReadProperties(player, before);

        Assert.IsFalse(transforms.WriteProperty(player, 0, [0, 0], ComponentPropertyValue.FromInteger(5), out _));
        Assert.IsFalse(transforms.WriteProperty(player, 1, [0, 0], ComponentPropertyValue.FromInteger(5), out _), "a Direct pool holds one instance");

        var after = new RecordingVisitor();
        transforms.ReadProperties(player, after);
        Assert.AreEqual(before.InstanceVersions[0], after.InstanceVersions[0]);
    }

    private static GameBuildPassResult BuildSimulatedWorld()
    {
        var pass = BuiltInTestModules.Build(new Map(new MapBounds(0, 0, 1024, 1024, 3)), sessionSeed: 1, initialEntityCapacity: 5000, initialComponentCapacity: 5000, crawlerNumbers: new Engine.Math.UniqueNumberAllocator(1, 1, 24));
        var world = pass.World;
        var ecsContext = pass.EcsContext;
        var context = pass.Context;
        var playerEntityId = FloorBuilder.ReservePlayerEntity(ecsContext);
        FloorBuilder.PopulateFloor(world, ecsContext, new NeighborhoodRecords(worldSeed: 1, floorNumber: FloorBuilder.FirstFloorNumber), pass.Factory, context.Terrain, context.Auras, context.Definitions, context.Areas, context.UniqueEntities);
        FloorBuilder.CreatePlayer(world, ecsContext, sessionSeed: 1, pass.Factory, context.Definitions, playerEntityId, FloorBuilder.PlayerSpawnOrigin(context.Areas));
        world.PlayerEntityId = playerEntityId;

        var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / FramesPerSecond);
        for (var frame = 1; frame <= 3 * FramesPerSecond; frame++)
        {
            ecsContext.SystemManager.Update(new EngineTime(frameDuration * frame, frameDuration, false, frame));
        }

        return pass;
    }
}
