using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Achievements;
using Game.Modules.Achievements.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Lootboxes;

namespace Tests.Modules.Achievements;

[TestClass]
public sealed class AchievementLootboxClaimsTests
{
    private const int PlayerEntityId = 0;

    private static readonly Guid AchievementId = Guid.NewGuid();

    private static (ComponentManager ComponentManager, AchievementLootboxClaims Claims, LootboxCatalog LootboxCatalog) CreateSetup()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        new InventoryModule().RegisterComponents(componentManager);
        new AchievementModule().RegisterComponents(componentManager);

        var lootboxCatalog = new LootboxCatalog(new ItemCatalog());
        foreach (var type in LootboxTypes.All)
        {
            lootboxCatalog.Register(type);
        }

        return (componentManager, new AchievementLootboxClaims(componentManager, lootboxCatalog, new EventBus()), lootboxCatalog);
    }

    private static List<InventoryItemStackComponent> Stacks(ComponentManager componentManager)
    {
        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(componentManager.GetMultiPool<InventoryItemStackComponent>(), PlayerEntityId, stacks);
        return stacks;
    }

    [TestMethod]
    public void TryClaim_OwedBox_GrantsItAndForgetsIt()
    {
        var (componentManager, claims, _) = CreateSetup();
        var reward = new LootboxReward(LootboxTypes.Investor.Id, LootboxRarity.Bronze);
        componentManager.GetMultiPool<UnclaimedAchievementLootboxComponent>().Add(PlayerEntityId, new UnclaimedAchievementLootboxComponent(AchievementId, reward));

        Assert.IsTrue(claims.TryClaim(PlayerEntityId, AchievementId));

        var stacks = Stacks(componentManager);
        Assert.HasCount(1, stacks);
        Assert.AreEqual(LootboxCatalog.ItemIdFor(reward.Kind), stacks[0].ItemDefinitionId);
        Assert.AreEqual(0, componentManager.GetMultiPool<UnclaimedAchievementLootboxComponent>().CountForEntity(PlayerEntityId));
    }

    [TestMethod]
    public void TryClaim_Twice_GrantsOnlyOnce()
    {
        var (componentManager, claims, _) = CreateSetup();
        componentManager.GetMultiPool<UnclaimedAchievementLootboxComponent>().Add(PlayerEntityId, new UnclaimedAchievementLootboxComponent(AchievementId, new LootboxReward(LootboxTypes.Investor.Id, LootboxRarity.Bronze)));

        claims.TryClaim(PlayerEntityId, AchievementId);

        Assert.IsFalse(claims.TryClaim(PlayerEntityId, AchievementId));
        Assert.AreEqual(1, Stacks(componentManager).Single().Quantity);
    }

    [TestMethod]
    public void TryClaim_AnotherAchievementsBox_LeavesItOwed()
    {
        var (componentManager, claims, _) = CreateSetup();
        componentManager.GetMultiPool<UnclaimedAchievementLootboxComponent>().Add(PlayerEntityId, new UnclaimedAchievementLootboxComponent(AchievementId, new LootboxReward(LootboxTypes.Investor.Id, LootboxRarity.Bronze)));

        Assert.IsFalse(claims.TryClaim(PlayerEntityId, Guid.NewGuid()));

        Assert.IsEmpty(Stacks(componentManager));
        Assert.AreEqual(1, componentManager.GetMultiPool<UnclaimedAchievementLootboxComponent>().CountForEntity(PlayerEntityId));
    }
}
