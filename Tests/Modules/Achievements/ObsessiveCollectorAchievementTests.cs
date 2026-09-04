using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules;
using Game.Modules.Achievements;
using Game.Modules.Achievements.Components;
using Game.Modules.Achievements.Definitions;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.World;

namespace Tests.Modules.Achievements;

/// <summary>
/// Exercises ObsessiveCollectorAchievement's polled trigger (see AchievementTriggerContext.
/// SubscribePolled) directly through InventoryModule + AchievementModule, without the full
/// Bootstrapper.Build module graph AchievementModuleTests uses -- InventoryModule.RegisterSystems
/// itself is never called here (it needs ActionsModule-configured dependencies this test doesn't
/// care about), only RegisterComponents, the same "just need the pools" shape
/// InventoryActionsTests.CreateRegisteredManager already uses.
/// </summary>
[TestClass]
public sealed class ObsessiveCollectorAchievementTests
{
    private static readonly Guid ObsessiveCollectorAchievementId = new ObsessiveCollectorAchievement().Id;

    private static (ComponentManager ComponentManager, SystemManager SystemManager, Game.World.World World) Build()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        var systemManager = new SystemManager();
        var eventBus = new EventBus();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));

        new InventoryModule().RegisterComponents(manager);

        var achievementModule = new AchievementModule();
        achievementModule.Configure(new GameModuleContext(world, new MathUtility(), eventBus) { PlayerQuery = world });
        achievementModule.RegisterComponents(manager);
        achievementModule.RegisterSystems(systemManager, manager);

        return (manager, systemManager, world);
    }

    private static void Tick(SystemManager systemManager) =>
        systemManager.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, IsRunningSlowly: false, FrameCount: 0));

    [TestMethod]
    public void StackReaching999_UnlocksAchievementAndRaisesMaxStackSizeTo1000()
    {
        var (manager, systemManager, world) = Build();
        var playerEntityId = 1;
        world.PlayerEntityId = playerEntityId;

        InventoryActions.AddItem(manager, playerEntityId, Guid.NewGuid(), quantity: 999);
        Tick(systemManager);

        var unlockedAchievements = manager.GetMultiPool<AchievementUnlockedComponent>();
        Assert.IsTrue(AchievementQueries.HasEarned(unlockedAchievements, playerEntityId, ObsessiveCollectorAchievementId));

        Assert.IsTrue(manager.GetPackedPool<MaxStackSizeComponent>().TryGetReadonly(playerEntityId, out var maxStackSize));
        Assert.AreEqual(1000, maxStackSize.Value);
    }

    [TestMethod]
    public void StackBelow999_NeverUnlocks()
    {
        var (manager, systemManager, world) = Build();
        var playerEntityId = 1;
        world.PlayerEntityId = playerEntityId;

        InventoryActions.AddItem(manager, playerEntityId, Guid.NewGuid(), quantity: 998);
        Tick(systemManager);

        var unlockedAchievements = manager.GetMultiPool<AchievementUnlockedComponent>();
        Assert.IsFalse(AchievementQueries.HasEarned(unlockedAchievements, playerEntityId, ObsessiveCollectorAchievementId));
    }

    [TestMethod]
    public void NonPlayerEntityReaching999_DoesNotUnlockAchievement()
    {
        var (manager, systemManager, world) = Build();
        var playerEntityId = 1;
        var otherEntityId = 2;
        world.PlayerEntityId = playerEntityId;

        InventoryActions.AddItem(manager, otherEntityId, Guid.NewGuid(), quantity: 999);
        Tick(systemManager);

        var unlockedAchievements = manager.GetMultiPool<AchievementUnlockedComponent>();
        Assert.IsFalse(AchievementQueries.HasEarned(unlockedAchievements, playerEntityId, ObsessiveCollectorAchievementId));
        Assert.IsFalse(AchievementQueries.HasEarned(unlockedAchievements, otherEntityId, ObsessiveCollectorAchievementId));
    }

    [TestMethod]
    public void MaxStackSizeReward_RaisesEffectiveCapOnFurtherGrants()
    {
        var (manager, systemManager, world) = Build();
        var playerEntityId = 1;
        world.PlayerEntityId = playerEntityId;
        var itemId = Guid.NewGuid();

        InventoryActions.AddItem(manager, playerEntityId, itemId, quantity: 999);
        Tick(systemManager); // unlocks Obsessive Collector, raises the player's own cap to 1000

        InventoryActions.AddItem(manager, playerEntityId, itemId, quantity: 1);

        var stacks = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(1, stacks.CountForEntity(playerEntityId));
        Assert.AreEqual(1000, stacks.GetReadonlyByDenseIndex(stacks.GetFirstDenseIndex(playerEntityId)).Quantity);
    }
}
