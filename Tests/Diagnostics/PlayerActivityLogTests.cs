using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Diagnostics;
using Game.Modules.Core.Components;
using Game.World;

namespace Tests.Diagnostics;

[TestClass]
public sealed class PlayerActivityLogTests
{
    private static string CreateTempLogPath() => Path.Combine(Path.GetTempPath(), $"player-activity-{Guid.NewGuid():N}.log");

    private static Game.World.World CreateWorld(int playerEntityId) =>
        new(new Map(new Vector3Int(10, 10, 1))) { PlayerEntityId = playerEntityId };

    private static ComponentManager CreateComponentManager()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        componentManager.RegisterDirectPool<DisplayTextComponent>(static (ref existing, incoming) => existing = incoming);
        return componentManager;
    }

    [TestMethod]
    public void EntityMoved_ForPlayer_IsLoggedWithFrameAndPositions()
    {
        var world = CreateWorld(playerEntityId: 0);
        var eventBus = new EventBus();
        var logPath = CreateTempLogPath();
        try
        {
            var log = new PlayerActivityLog(world, CreateComponentManager(), eventBus, logPath);
            log.BeginFrame(5, new DateTime(2026, 1, 1));

            eventBus.Publish(new EntityMovedEvent(0, new Vector3Int(1, 1, 0), new Vector3Int(2, 1, 0), new Vector2Byte(1, 1)));
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
            using var log = new PlayerActivityLog(world, CreateComponentManager(), eventBus, logPath);
            log.BeginFrame(1, DateTime.Now);

            eventBus.Publish(new EntityMovedEvent(1, new Vector3Int(1, 1, 0), new Vector3Int(2, 1, 0), new Vector2Byte(1, 1)));

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
            var log = new PlayerActivityLog(world, CreateComponentManager(), eventBus, logPath);
            log.BeginFrame(9, DateTime.Now);

            eventBus.Publish(new EntityDamagedEvent(0, 7, StatusEffectSource.Admin, 93, 100, "Status Effect (Burning)"));
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
            using var log = new PlayerActivityLog(world, CreateComponentManager(), eventBus, logPath);
            log.BeginFrame(1, DateTime.Now);

            eventBus.Publish(new EntityDamagedEvent(1, 7, StatusEffectSource.Admin, 93, 100, "Status Effect (Burning)"));

            Assert.AreEqual(0L, new FileInfo(logPath).Length);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    /// <summary>HealthDamage.Apply already publishes this case (player as Source, an NPC as the damaged EntityId) -- this guards that the log's own filter doesn't drop it.</summary>
    [TestMethod]
    public void EntityDamaged_PlayerIsSourceOfDamageToNonPlayer_IsLoggedWithTarget()
    {
        var world = CreateWorld(playerEntityId: 0);
        var eventBus = new EventBus();
        var logPath = CreateTempLogPath();
        try
        {
            var log = new PlayerActivityLog(world, CreateComponentManager(), eventBus, logPath);
            log.BeginFrame(3, DateTime.Now);

            eventBus.Publish(new EntityDamagedEvent(5, 12, StatusEffectSource.FromEntity(0), 88, 100, "Default Attack"));
            log.Dispose();

            var contents = File.ReadAllText(logPath);
            StringAssert.Contains(contents, "DAMAGE");
            StringAssert.Contains(contents, "amount=12");
            StringAssert.Contains(contents, "target=5");
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [TestMethod]
    public void EntityDamaged_TargetHasDisplayTextComponent_LogsNameAlongsideEntityId()
    {
        var world = CreateWorld(playerEntityId: 0);
        var eventBus = new EventBus();
        var logPath = CreateTempLogPath();
        var componentManager = CreateComponentManager();
        componentManager.Merge(5, new DisplayTextComponent("Goblin1", "A goblin."));
        try
        {
            var log = new PlayerActivityLog(world, componentManager, eventBus, logPath);
            log.BeginFrame(4, DateTime.Now);

            eventBus.Publish(new EntityDamagedEvent(5, 12, StatusEffectSource.FromEntity(0), 88, 100, "Default Attack"));
            log.Dispose();

            var contents = File.ReadAllText(logPath);
            StringAssert.Contains(contents, "target=5 (Goblin1)");
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [TestMethod]
    public void EntityDamaged_SourceEntityHasDisplayTextComponent_LogsNameAlongsideEntityId()
    {
        var world = CreateWorld(playerEntityId: 0);
        var eventBus = new EventBus();
        var logPath = CreateTempLogPath();
        var componentManager = CreateComponentManager();
        componentManager.Merge(0, new DisplayTextComponent("PlayerOne", "The player."));
        try
        {
            var log = new PlayerActivityLog(world, componentManager, eventBus, logPath);
            log.BeginFrame(4, DateTime.Now);

            eventBus.Publish(new EntityDamagedEvent(5, 12, StatusEffectSource.FromEntity(0), 88, 100, "Default Attack"));
            log.Dispose();

            var contents = File.ReadAllText(logPath);
            StringAssert.Contains(contents, "source=Entity#0 (PlayerOne)");
        }
        finally
        {
            File.Delete(logPath);
        }
    }
}
