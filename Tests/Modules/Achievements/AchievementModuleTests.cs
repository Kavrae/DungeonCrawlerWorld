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
/// against AchievementModule's internals directly. Deliberately publishes the spawn-sentinel
/// EntityMoved *before* assigning World.PlayerEntityId in the Loner tests below, matching
/// GameLoop/FloorBuilder.CreatePlayer's real ordering -- assigning it first (as an earlier
/// version of this file did) would have hidden the exact bug LonerAchievement's own doc
/// comment describes: PlayerEntityId isn't set yet at the moment this event actually fires.
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

    private static void PublishSpawn(EventBus eventBus, int entityId, Vector3Int position) =>
        eventBus.Publish(new EntityMoved(entityId, position, position, new Vector2Byte(1, 1)));

    private static readonly Guid LonerAchievementId = new LonerAchievement().Id;

    private static readonly Guid InflictedDamageAchievementId = new InflictedDamageAchievement().Id;

    [TestMethod]
    public void PlayerSpawnSentinel_UnlocksLonerAndPublishesMinimizedNotification()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        NotificationRequested? published = null;
        eventBus.Subscribe<NotificationRequested>(requested => published = requested);

        PublishSpawn(eventBus, playerEntityId, new Vector3Int(1, 1, 0));
        world.PlayerEntityId = playerEntityId; // assigned after publishing, matching GameLoop's real ordering
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
    public void PlayerSpawnSentinel_PublishedTwice_OnlyUnlocksAndNotifiesOnce()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        var notificationCount = 0;
        eventBus.Subscribe<NotificationRequested>(_ => notificationCount++);

        PublishSpawn(eventBus, playerEntityId, new Vector3Int(1, 1, 0));
        world.PlayerEntityId = playerEntityId;
        PublishSpawn(eventBus, playerEntityId, new Vector3Int(2, 2, 0));
        eventBus.DispatchBuffered<NotificationRequested>();

        Assert.AreEqual(1, notificationCount);

        var unlockedAchievements = ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>();
        var earnedCount = 0;
        for (var denseIndex = unlockedAchievements.GetFirstDenseIndex(playerEntityId); denseIndex != -1; denseIndex = unlockedAchievements.GetNextDenseIndex(denseIndex))
        {
            earnedCount++;
        }

        Assert.AreEqual(1, earnedCount);
    }

    [TestMethod]
    public void RealMove_OldPositionDiffersFromNew_DoesNotUnlockLoner()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;

        eventBus.Publish(new EntityMoved(playerEntityId, new Vector3Int(1, 1, 0), new Vector3Int(2, 1, 0), new Vector2Byte(1, 1)));

        Assert.IsFalse(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            LonerAchievementId));
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
