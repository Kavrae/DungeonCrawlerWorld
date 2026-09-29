using Engine.ECS.Components;
using Engine.Events;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Definitions;
using Game.Modules.Lootboxes;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Lootboxes;

[TestClass]
public sealed class LootboxOpenerTests
{
    private const int PlayerEntityId = 0;

    private static readonly ItemDefinition Potion = new(Guid.NewGuid(), "Potion", SpriteName: null, Glyph: "p", Color.White, Tags: [Tag.Potion], Effects: []);
    private static readonly ItemDefinition Scroll = new(Guid.NewGuid(), "Scroll", SpriteName: null, Glyph: "s", Color.White, Tags: [Tag.Scroll], Effects: []);

    private sealed record Setup(ComponentManager ComponentManager, ItemCatalog ItemCatalog, LootboxCatalog LootboxCatalog, EventBus EventBus)
    {
        public LootboxOpener CreateOpener(ulong seed = 1) => new(ComponentManager, LootboxCatalog, ItemCatalog, seed);

        public void Grant(LootboxReward reward, ushort quantity = 1) => LootboxActions.Grant(ComponentManager, LootboxCatalog, EventBus, PlayerEntityId, reward, quantity);

        public List<InventoryItemStackComponent> Stacks()
        {
            var stacks = new List<InventoryItemStackComponent>();
            InventoryQueries.CopyStacksForEntity(ComponentManager.GetMultiPool<InventoryItemStackComponent>(), PlayerEntityId, stacks);
            return stacks;
        }
    }

    private static Setup CreateSetup(params ItemDefinition[] items)
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        new InventoryModule().RegisterComponents(componentManager);
        new AbilityScoresModule().RegisterComponents(componentManager);

        var itemCatalog = new ItemCatalog();
        foreach (var item in items.Length > 0 ? items : [Potion, Scroll])
        {
            itemCatalog.Register(item);
        }

        var lootboxCatalog = new LootboxCatalog(itemCatalog);
        itemCatalog.AddDefinitionSource(lootboxCatalog);
        foreach (var type in LootboxTypes.All)
        {
            lootboxCatalog.Register(type);
        }

        return new Setup(componentManager, itemCatalog, lootboxCatalog, new EventBus());
    }

    private static LootboxReward Box(LootboxTypeDefinition type, LootboxRarity rarity, IItemContents? contents = null) => new(type.Id, rarity, contents);

    private static SetItemContents Contents(params ItemContentsEntry[] entries) => new(entries);

    [TestMethod]
    public void OpenAll_OpensLowestRarityFirst_ThenByTypeName()
    {
        var setup = CreateSetup();
        setup.Grant(Box(LootboxTypes.Weapon, LootboxRarity.Gold));
        setup.Grant(Box(LootboxTypes.Weapon, LootboxRarity.Bronze));
        setup.Grant(Box(LootboxTypes.Adventurer, LootboxRarity.Gold));
        setup.Grant(Box(LootboxTypes.Alchemist, LootboxRarity.Bronze));

        var opened = setup.CreateOpener().OpenAll(PlayerEntityId);

        CollectionAssert.AreEqual(
            new[]
            {
                new LootboxKind(LootboxTypes.Alchemist.Id, LootboxRarity.Bronze),
                new LootboxKind(LootboxTypes.Weapon.Id, LootboxRarity.Bronze),
                new LootboxKind(LootboxTypes.Adventurer.Id, LootboxRarity.Gold),
                new LootboxKind(LootboxTypes.Weapon.Id, LootboxRarity.Gold),
            },
            opened.Select(group => group.Kind).ToArray());
    }

    [TestMethod]
    public void OpenAll_ConsumesEveryBox_AndLeavesOtherItemsAlone()
    {
        var setup = CreateSetup();
        InventoryActions.AddItem(setup.ComponentManager, PlayerEntityId, Scroll.Id, quantity: 4);
        setup.Grant(Box(LootboxTypes.Adventurer, LootboxRarity.Bronze), quantity: 3);
        setup.Grant(Box(LootboxTypes.Weapon, LootboxRarity.Silver));

        setup.CreateOpener().OpenAll(PlayerEntityId);

        var stacks = setup.Stacks();
        Assert.IsFalse(stacks.Any(stack => setup.LootboxCatalog.TryGetKind(stack.ItemDefinitionId, out _)));
        Assert.IsTrue(stacks.Any(stack => stack.ItemDefinitionId == Scroll.Id));
    }

    [TestMethod]
    public void OpenAll_UsualAndOverriddenBoxesOfOneKind_AreOneGroup_WithTheirItemsCombined()
    {
        var setup = CreateSetup();
        setup.Grant(Box(LootboxTypes.Boss, LootboxRarity.Gold, Contents(new ItemContentsEntry(Potion.Id, 2))), quantity: 2);
        setup.Grant(Box(LootboxTypes.Boss, LootboxRarity.Gold, Contents(new ItemContentsEntry(Potion.Id, 3), new ItemContentsEntry(Scroll.Id, 1))));

        var opened = setup.CreateOpener().OpenAll(PlayerEntityId);

        var group = opened.Single();
        Assert.AreEqual(3, group.Count);
        Assert.AreEqual(7, group.Items.Single(item => item.ItemDefinitionId == Potion.Id).Quantity);
        Assert.AreEqual(1, group.Items.Single(item => item.ItemDefinitionId == Scroll.Id).Quantity);
        Assert.AreEqual(7, setup.Stacks().Where(stack => stack.ItemDefinitionId == Potion.Id).Sum(stack => stack.Quantity));
    }

    [TestMethod]
    public void OpenAll_GrantedItemStackInstanceId_IsTheStackTheItemLandedIn()
    {
        var setup = CreateSetup();
        setup.Grant(Box(LootboxTypes.Alchemist, LootboxRarity.Bronze, Contents(new ItemContentsEntry(Potion.Id, 2))));

        var grantedItem = setup.CreateOpener().OpenAll(PlayerEntityId).Single().Items.Single();

        Assert.AreEqual(setup.Stacks().Single(stack => stack.ItemDefinitionId == Potion.Id).StackInstanceId, grantedItem.StackInstanceId);
    }

    [TestMethod]
    public void OpenAll_UsualBox_GrantsOneStackOfOneToTenOfATradeableItem()
    {
        var setup = CreateSetup();
        setup.Grant(Box(LootboxTypes.Adventurer, LootboxRarity.Bronze), quantity: 20);

        var group = setup.CreateOpener().OpenAll(PlayerEntityId).Single();

        Assert.AreEqual(20, group.Count);
        Assert.IsTrue(group.Items.All(item => item.ItemDefinitionId == Potion.Id || item.ItemDefinitionId == Scroll.Id));
        Assert.IsTrue(group.Items.Sum(item => item.Quantity) is >= 20 and <= 200);
    }

    [TestMethod]
    public void OpenAll_SameSeed_GrantsTheSameItems()
    {
        List<GrantedItem> OpenWithSeed(ulong seed)
        {
            var setup = CreateSetup();
            setup.Grant(Box(LootboxTypes.Adventurer, LootboxRarity.Bronze), quantity: 5);
            return [.. setup.CreateOpener(seed).OpenAll(PlayerEntityId).Single().Items.Select(item => item with { StackInstanceId = 0 })];
        }

        CollectionAssert.AreEqual(OpenWithSeed(9), OpenWithSeed(9));
    }

    [TestMethod]
    public void OpenAll_WithNoBoxes_OpensNothing()
    {
        var setup = CreateSetup();
        InventoryActions.AddItem(setup.ComponentManager, PlayerEntityId, Potion.Id, quantity: 1);

        Assert.IsEmpty(setup.CreateOpener().OpenAll(PlayerEntityId));
        Assert.HasCount(1, setup.Stacks());
    }

    [TestMethod]
    public void OpenAll_WandReward_IsGrantedWithItsChargesBaked()
    {
        var wand = WandOfFireball.Build();
        var setup = CreateSetup(wand);
        setup.Grant(Box(LootboxTypes.Weapon, LootboxRarity.Bronze, Contents(new ItemContentsEntry(wand.Id, 1))));

        setup.CreateOpener().OpenAll(PlayerEntityId);

        var wandStack = setup.Stacks().Single(stack => stack.ItemDefinitionId == wand.Id);
        Assert.IsInstanceOfType<WandActivator>(wandStack.Override?.Activator);
    }
}
