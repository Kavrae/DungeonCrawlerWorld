using Engine.Bootstrap;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.Achievements;
using Game.Modules.Achievements.Components;
using Game.Modules.Achievements.Definitions;
using Game.Notifications;
using Game.World;

namespace Tests.Modules.Achievements;

/// <summary>
/// Exercises the built-in achievements end-to-end through the real AchievementModule (Configure
/// then RegisterSystems, mirroring GameBootstrapper.Build's own ordering -- see
/// GameModuleIntegrationTests for the same pattern with other real modules) rather than
/// against AchievementModule's internals directly. The Loner/UnarmedCombat tests below assign
/// World.PlayerEntityId *before* publishing EnteredDungeon, matching GameLoop's real ordering
/// (EnteredDungeon is published only after _playerSpawned flips true, which happens after
/// World.PlayerEntityId is assigned).
/// </summary>
[TestClass]
public sealed class AchievementModuleTests
{
    private static (EcsContext EcsContext, EventBus EventBus, Game.World.World World) Build()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var eventBus = new EventBus();

        var module = new AchievementModule();
        module.Configure(new GameModuleContext(world, new MathUtility(), eventBus) { PlayerQuery = world });

        IReadOnlyList<IModule> modules = [module];
        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: 10, initialComponentCapacity: 10, eventBus);

        return (ecsContext, eventBus, world);
    }

    private static readonly Guid LonerAchievementId = new LonerAchievement().Id;

    private static readonly Guid InflictedDamageAchievementId = new InflictedDamageAchievement().Id;

    private static readonly Guid UnarmedCombatAchievementId = new UnarmedCombatAchievement().Id;

    [TestMethod]
    public void EnteredDungeon_WithoutPlayerQuery_NeverSubscribesSoNothingUnlocks()
    {
        var eventBus = new EventBus();

        var module = new AchievementModule();
        module.Configure(new GameModuleContext(new Game.World.World(new Map(new Vector3Int(5, 5, 1))), new MathUtility(), eventBus)); // PlayerQuery left null

        IReadOnlyList<IModule> modules = [module];
        Bootstrapper.Build(modules, initialEntityCapacity: 10, initialComponentCapacity: 10, eventBus);

        var notificationCount = 0;
        eventBus.Subscribe<NotificationRequested>(_ => notificationCount++);

        eventBus.Publish(new EnteredDungeon());
        eventBus.DispatchBuffered<NotificationRequested>();

        Assert.AreEqual(0, notificationCount);
    }

    [TestMethod]
    public void EnteredDungeon_UnlocksLonerAndPublishesMinimizedNotification()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        NotificationRequested? published = null;
        eventBus.Subscribe<NotificationRequested>(requested =>
        {
            if (requested.Title == "Loner")
            {
                published = requested;
            }
        });

        eventBus.Publish(new EnteredDungeon());
        eventBus.DispatchBuffered<NotificationRequested>(); // NotificationRequested is buffered -- see NotificationCenter.Update's own doc comment.

        Assert.IsTrue(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            LonerAchievementId));

        Assert.IsNotNull(published);
        Assert.AreEqual(NotificationCategory.Achievement, published!.Category);
        Assert.AreEqual("Loner", published.Title);
        Assert.IsFalse(published.ShowImmediately);
        Assert.IsNotNull(published.Achievement);
        Assert.IsNull(published.Achievement!.LootboxLabel);
    }

    [TestMethod]
    public void EnteredDungeon_PublishedTwice_OnlyUnlocksAndNotifiesOnce()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        var lonerNotificationCount = 0;
        eventBus.Subscribe<NotificationRequested>(requested =>
        {
            if (requested.Title == "Loner")
            {
                lonerNotificationCount++;
            }
        });

        eventBus.Publish(new EnteredDungeon());
        eventBus.Publish(new EnteredDungeon());
        eventBus.DispatchBuffered<NotificationRequested>();

        Assert.AreEqual(1, lonerNotificationCount);

        var unlockedAchievements = ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>();
        var earnedCount = 0;
        for (var denseIndex = unlockedAchievements.GetFirstDenseIndex(playerEntityId); denseIndex != -1; denseIndex = unlockedAchievements.GetNextDenseIndex(denseIndex))
        {
            earnedCount++;
        }

        Assert.AreEqual(2, earnedCount);
    }

    [TestMethod]
    public void EntityMoved_DoesNotUnlockLoner()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;

        eventBus.Publish(new EntityMoved(playerEntityId, new Vector3Int(1, 1, 0), new Vector3Int(1, 1, 0), new Vector2Byte(1, 1)));

        Assert.IsFalse(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            LonerAchievementId));
    }

    [TestMethod]
    public void EnteredDungeon_UnlocksUnarmedCombatAndPublishesLootboxNotification()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        NotificationRequested? published = null;
        eventBus.Subscribe<NotificationRequested>(requested =>
        {
            if (requested.Title == "Unarmed Combat")
            {
                published = requested;
            }
        });

        eventBus.Publish(new EnteredDungeon());
        eventBus.DispatchBuffered<NotificationRequested>(); // NotificationRequested is buffered -- see NotificationCenter.Update's own doc comment.

        Assert.IsTrue(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            UnarmedCombatAchievementId));

        Assert.IsNotNull(published);
        Assert.AreEqual(NotificationCategory.Achievement, published!.Category);
        Assert.IsNotNull(published.Achievement);
        Assert.AreEqual("Bronze Weapon Box", published.Achievement!.LootboxLabel);
    }

    [TestMethod]
    public void EntityMoved_DoesNotUnlockUnarmedCombat()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;

        eventBus.Publish(new EntityMoved(playerEntityId, new Vector3Int(1, 1, 0), new Vector3Int(1, 1, 0), new Vector2Byte(1, 1)));

        Assert.IsFalse(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            UnarmedCombatAchievementId));
    }

    [TestMethod]
    public void PlayerDamagesNpc_UnlocksInflictedDamageAndPublishesNotification()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        var npcEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        NotificationRequested? published = null;
        eventBus.Subscribe<NotificationRequested>(requested => published = requested);

        eventBus.Publish(new EntityDamaged(npcEntityId, 5, StatusEffectSource.FromEntity(playerEntityId), 15, 20, "Default Attack"));
        eventBus.DispatchBuffered<NotificationRequested>();

        Assert.IsTrue(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            InflictedDamageAchievementId));

        Assert.IsNotNull(published);
        Assert.AreEqual(NotificationCategory.Achievement, published!.Category);
        Assert.AreEqual("You've Inflicted Damage on a Mob", published.Title);
    }

    [TestMethod]
    public void NpcDamagesPlayer_DoesNotUnlockInflictedDamage()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        var npcEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;

        eventBus.Publish(new EntityDamaged(playerEntityId, 5, StatusEffectSource.FromEntity(npcEntityId), 15, 20, "Contact"));

        Assert.IsFalse(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            InflictedDamageAchievementId));
    }
}
