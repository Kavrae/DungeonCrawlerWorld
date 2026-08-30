using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Diagnostics;
using Game.Modules.Core.Components;
using Game.Modules.StatusEffects;
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
            StringAssert.Contains(contents, "target=Goblin1 (#5)");
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
            StringAssert.Contains(contents, "source=PlayerOne (#0)");
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [TestMethod]
    public void EntityHealed_ForPlayer_IsLoggedWithAmountAndSource()
    {
        var world = CreateWorld(playerEntityId: 0);
        var eventBus = new EventBus();
        var logPath = CreateTempLogPath();
        try
        {
            var log = new PlayerActivityLog(world, CreateComponentManager(), eventBus, logPath);
            log.BeginFrame(9, DateTime.Now);

            eventBus.Publish(new EntityHealedEvent(0, 12f, StatusEffectSource.Admin, 93f, 100f, "Regeneration"));
            log.Dispose();

            var contents = File.ReadAllText(logPath);
            StringAssert.Contains(contents, "HEAL");
            StringAssert.Contains(contents, "amount=12");
            StringAssert.Contains(contents, "Regeneration");
            StringAssert.Contains(contents, "Admin");
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [TestMethod]
    public void EntityHealed_ForNonPlayer_IsNotLogged()
    {
        var world = CreateWorld(playerEntityId: 0);
        var eventBus = new EventBus();
        var logPath = CreateTempLogPath();
        try
        {
            using var log = new PlayerActivityLog(world, CreateComponentManager(), eventBus, logPath);
            log.BeginFrame(1, DateTime.Now);

            eventBus.Publish(new EntityHealedEvent(1, 12f, StatusEffectSource.Admin, 93f, 100f, "Regeneration"));

            Assert.AreEqual(0L, new FileInfo(logPath).Length);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    /// <summary>HealthHeal.Apply already publishes this case (player as Source, an NPC as the healed EntityId) -- this guards that the log's own filter doesn't drop it, mirroring EntityDamaged_PlayerIsSourceOfDamageToNonPlayer_IsLoggedWithTarget.</summary>
    [TestMethod]
    public void EntityHealed_PlayerIsSourceOfHealToNonPlayer_IsLoggedWithTarget()
    {
        var world = CreateWorld(playerEntityId: 0);
        var eventBus = new EventBus();
        var logPath = CreateTempLogPath();
        try
        {
            var log = new PlayerActivityLog(world, CreateComponentManager(), eventBus, logPath);
            log.BeginFrame(3, DateTime.Now);

            eventBus.Publish(new EntityHealedEvent(5, 20f, StatusEffectSource.FromEntity(0), 88f, 100f, "Heal"));
            log.Dispose();

            var contents = File.ReadAllText(logPath);
            StringAssert.Contains(contents, "HEAL");
            StringAssert.Contains(contents, "amount=20");
            StringAssert.Contains(contents, "target=5");
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [TestMethod]
    public void StatusEffectImmunityBlocked_ForPlayer_IsLoggedWithTypeAndSource()
    {
        var world = CreateWorld(playerEntityId: 0);
        var eventBus = new EventBus();
        var logPath = CreateTempLogPath();
        try
        {
            var log = new PlayerActivityLog(world, CreateComponentManager(), eventBus, logPath);
            log.BeginFrame(9, DateTime.Now);

            eventBus.Publish(new StatusEffectImmunityBlockedEvent(0, StatusEffectType.Burning, StatusEffectSource.FromEntity(7)));
            log.Dispose();

            var contents = File.ReadAllText(logPath);
            StringAssert.Contains(contents, "BLOCKED");
            StringAssert.Contains(contents, "Burning");
            StringAssert.Contains(contents, "target=0");
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [TestMethod]
    public void StatusEffectImmunityBlocked_ForNonPlayer_IsNotLogged()
    {
        var world = CreateWorld(playerEntityId: 0);
        var eventBus = new EventBus();
        var logPath = CreateTempLogPath();
        try
        {
            using var log = new PlayerActivityLog(world, CreateComponentManager(), eventBus, logPath);
            log.BeginFrame(1, DateTime.Now);

            eventBus.Publish(new StatusEffectImmunityBlockedEvent(1, StatusEffectType.Poison, StatusEffectSource.Admin));

            Assert.AreEqual(0L, new FileInfo(logPath).Length);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    /// <summary>Mirrors EntityDamaged_PlayerIsSourceOfDamageToNonPlayer_IsLoggedWithTarget -- the player attempting (and being blocked) to inflict an effect on an immune NPC must still be logged.</summary>
    [TestMethod]
    public void StatusEffectImmunityBlocked_PlayerIsSourceOnNonPlayerTarget_IsLoggedWithTarget()
    {
        var world = CreateWorld(playerEntityId: 0);
        var eventBus = new EventBus();
        var logPath = CreateTempLogPath();
        try
        {
            var log = new PlayerActivityLog(world, CreateComponentManager(), eventBus, logPath);
            log.BeginFrame(3, DateTime.Now);

            eventBus.Publish(new StatusEffectImmunityBlockedEvent(5, StatusEffectType.Poison, StatusEffectSource.FromEntity(0)));
            log.Dispose();

            var contents = File.ReadAllText(logPath);
            StringAssert.Contains(contents, "BLOCKED");
            StringAssert.Contains(contents, "target=5");
        }
        finally
        {
            File.Delete(logPath);
        }
    }
}
