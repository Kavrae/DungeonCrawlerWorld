using Engine.Events;
using Engine.Math;
using Game.Diagnostics;
using Game.World;

namespace Tests.Diagnostics;

[TestClass]
public sealed class PlayerActivityLogTests
{
    private static string CreateTempLogPath() => Path.Combine(Path.GetTempPath(), $"player-activity-{Guid.NewGuid():N}.log");

    private static Game.World.World CreateWorld(int playerEntityId) =>
        new(new Map(new Vector3Int(10, 10, 1))) { PlayerEntityId = playerEntityId };

    [TestMethod]
    public void EntityMoved_ForPlayer_IsLoggedWithFrameAndPositions()
    {
        var world = CreateWorld(playerEntityId: 0);
        var eventBus = new EventBus();
        var logPath = CreateTempLogPath();
        try
        {
            var log = new PlayerActivityLog(world, eventBus, logPath);
            log.BeginFrame(5, new DateTime(2026, 1, 1));

            eventBus.Publish(new EntityMoved(0, new Vector3Int(1, 1, 0), new Vector3Int(2, 1, 0), new Vector2Byte(1, 1)));
            log.Dispose();

            var contents = File.ReadAllText(logPath);
            StringAssert.Contains(contents, "MOVE");
            StringAssert.Contains(contents, "Frame 5");
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [TestMethod]
    public void EntityMoved_ForNonPlayer_IsNotLogged()
    {
        var world = CreateWorld(playerEntityId: 0);
        var eventBus = new EventBus();
        var logPath = CreateTempLogPath();
        try
        {
            using var log = new PlayerActivityLog(world, eventBus, logPath);
            log.BeginFrame(1, DateTime.Now);

            eventBus.Publish(new EntityMoved(1, new Vector3Int(1, 1, 0), new Vector3Int(2, 1, 0), new Vector2Byte(1, 1)));

            Assert.AreEqual(0L, new FileInfo(logPath).Length);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [TestMethod]
    public void EntityDamaged_ForPlayer_IsLoggedWithAmountAndSource()
    {
        var world = CreateWorld(playerEntityId: 0);
        var eventBus = new EventBus();
        var logPath = CreateTempLogPath();
        try
        {
            var log = new PlayerActivityLog(world, eventBus, logPath);
            log.BeginFrame(9, DateTime.Now);

            eventBus.Publish(new EntityDamaged(0, 7, StatusEffectSource.Admin, 93, 100, "Status Effect (Burning)"));
            log.Dispose();

            var contents = File.ReadAllText(logPath);
            StringAssert.Contains(contents, "DAMAGE");
            StringAssert.Contains(contents, "amount=7");
            StringAssert.Contains(contents, "Status Effect (Burning)");
            StringAssert.Contains(contents, "Admin");
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [TestMethod]
    public void EntityDamaged_ForNonPlayer_IsNotLogged()
    {
        var world = CreateWorld(playerEntityId: 0);
        var eventBus = new EventBus();
        var logPath = CreateTempLogPath();
        try
        {
            using var log = new PlayerActivityLog(world, eventBus, logPath);
            log.BeginFrame(1, DateTime.Now);

            eventBus.Publish(new EntityDamaged(1, 7, StatusEffectSource.Admin, 93, 100, "Status Effect (Burning)"));

            Assert.AreEqual(0L, new FileInfo(logPath).Length);
        }
        finally
        {
            File.Delete(logPath);
        }
    }
}
