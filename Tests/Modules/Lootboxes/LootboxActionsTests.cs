using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Lootboxes;

namespace Tests.Modules.Lootboxes;

[TestClass]
public sealed class LootboxActionsTests
{
    private const int EntityId = 0;

    private static (ComponentManager ComponentManager, LootboxCatalog LootboxCatalog, EventBus EventBus) CreateSetup()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        new InventoryModule().RegisterComponents(componentManager);

        var itemCatalog = new ItemCatalog();
        var lootboxCatalog = new LootboxCatalog(itemCatalog);
        itemCatalog.AddDefinitionSource(lootboxCatalog);
        foreach (var type in LootboxTypes.All)
        {
            lootboxCatalog.Register(type);
        }

        return (componentManager, lootboxCatalog, new EventBus());
    }

    private static List<InventoryItemStackComponent> Stacks(ComponentManager componentManager)
    {
        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(componentManager.GetMultiPool<InventoryItemStackComponent>(), EntityId, stacks);
        return stacks;
    }

    private static SetItemContents ContentsOf(Guid itemId) => new([new ItemContentsEntry(itemId, 1)]);

    [TestMethod]
    public void Grant_SameTypeAndRarityTwice_StacksIntoOneStackOfTwo()
    {
        var (componentManager, lootboxCatalog, eventBus) = CreateSetup();
        var reward = new LootboxReward(LootboxTypes.Adventurer.Id, LootboxRarity.Bronze);

        LootboxActions.Grant(componentManager, lootboxCatalog, eventBus, EntityId, reward);
        LootboxActions.Grant(componentManager, lootboxCatalog, eventBus, EntityId, reward);

        var stacks = Stacks(componentManager);
        Assert.HasCount(1, stacks);
        Assert.AreEqual(2, stacks[0].Quantity);
    }

    [TestMethod]
    public void Grant_DifferentRaritiesOfOneType_MakesSeparateStacks()
    {
        var (componentManager, lootboxCatalog, eventBus) = CreateSetup();

        LootboxActions.Grant(componentManager, lootboxCatalog, eventBus, EntityId, new LootboxReward(LootboxTypes.Adventurer.Id, LootboxRarity.Bronze));
        LootboxActions.Grant(componentManager, lootboxCatalog, eventBus, EntityId, new LootboxReward(LootboxTypes.Adventurer.Id, LootboxRarity.Silver));

        Assert.HasCount(2, Stacks(componentManager));
    }

    [TestMethod]
    public void Grant_EqualContentsBuiltSeparately_StackTogether()
    {
        var (componentManager, lootboxCatalog, eventBus) = CreateSetup();
        var itemId = Guid.NewGuid();

        LootboxActions.Grant(componentManager, lootboxCatalog, eventBus, EntityId, new LootboxReward(LootboxTypes.Boss.Id, LootboxRarity.Gold, ContentsOf(itemId)));
        LootboxActions.Grant(componentManager, lootboxCatalog, eventBus, EntityId, new LootboxReward(LootboxTypes.Boss.Id, LootboxRarity.Gold, ContentsOf(itemId)));

        var stacks = Stacks(componentManager);
        Assert.HasCount(1, stacks);
        Assert.AreEqual(2, stacks[0].Quantity);
    }

    [TestMethod]
    public void Grant_OverriddenAndUsualBoxOfOneKind_NeverStackTogether_InEitherOrder()
    {
        var (componentManager, lootboxCatalog, eventBus) = CreateSetup();
        var usual = new LootboxReward(LootboxTypes.Boss.Id, LootboxRarity.Gold);
        var overridden = usual with { Contents = ContentsOf(Guid.NewGuid()) };

        LootboxActions.Grant(componentManager, lootboxCatalog, eventBus, EntityId, overridden);
        LootboxActions.Grant(componentManager, lootboxCatalog, eventBus, EntityId, usual);
        LootboxActions.Grant(componentManager, lootboxCatalog, eventBus, EntityId, overridden);

        var stacks = Stacks(componentManager);
        Assert.HasCount(2, stacks);
        Assert.AreEqual(1, stacks.Single(stack => stack.Override is null).Quantity);
        Assert.AreEqual(2, stacks.Single(stack => stack.Override is not null).Quantity);
    }

    [TestMethod]
    public void Grant_DifferentContents_MakeSeparateStacks()
    {
        var (componentManager, lootboxCatalog, eventBus) = CreateSetup();

        LootboxActions.Grant(componentManager, lootboxCatalog, eventBus, EntityId, new LootboxReward(LootboxTypes.Boss.Id, LootboxRarity.Gold, ContentsOf(Guid.NewGuid())));
        LootboxActions.Grant(componentManager, lootboxCatalog, eventBus, EntityId, new LootboxReward(LootboxTypes.Boss.Id, LootboxRarity.Gold, ContentsOf(Guid.NewGuid())));

        Assert.HasCount(2, Stacks(componentManager));
    }

    [TestMethod]
    public void Grant_PublishesLootboxGrantedEvent()
    {
        var (componentManager, lootboxCatalog, eventBus) = CreateSetup();
        var reward = new LootboxReward(LootboxTypes.Weapon.Id, LootboxRarity.Bronze);
        LootboxGrantedEvent? published = null;
        eventBus.Subscribe<LootboxGrantedEvent>(granted => published = granted);

        LootboxActions.Grant(componentManager, lootboxCatalog, eventBus, EntityId, reward, quantity: 3);

        Assert.AreEqual(new LootboxGrantedEvent(EntityId, reward, 3), published);
    }
}
