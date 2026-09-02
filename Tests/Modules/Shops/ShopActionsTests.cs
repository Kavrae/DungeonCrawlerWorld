using Engine.ECS.Components;
using Game.Modules;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops;
using Game.Modules.Shops.Components;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Shops;

[TestClass]
public sealed class ShopActionsTests
{
    private const int PlayerEntityId = 0;
    private const int ShopEntityId = 1;

    private const int PotionValue = 10;
    private const int ToolValue = 20;

    private static readonly Guid PotionItemId = Guid.NewGuid();
    private static readonly Guid ToolItemId = Guid.NewGuid();

    private static readonly ShopComponent PotionOnlyShop = new(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f);
    private static readonly ShopComponent GeneralShop = new(allowedTags: null, buyMultiplier: 1.20f, sellMultiplier: 0.80f);

    private sealed class FakePlayerQuery(int playerEntityId) : IPlayerQuery
    {
        public int PlayerEntityId { get; } = playerEntityId;
    }

    private static readonly FakePlayerQuery NoEntityIsThePlayer = new(playerEntityId: -1);

    private static (ComponentManager Manager, ItemCatalog Catalog) BuildManager()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8);
        new CurrencyModule().RegisterComponents(manager);
        new ShopModule().RegisterComponents(manager);
        manager.RegisterMultiPool<InventoryItemStackComponent>();
        manager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);

        var catalog = new ItemCatalog();
        catalog.Register(new ItemDefinition(PotionItemId, "Test Potion", null, "p", Color.White, Tags: [Tag.Potion], Effects: [], GoldValue: PotionValue));
        catalog.Register(new ItemDefinition(ToolItemId, "Test Tool", null, "t", Color.White, Tags: [Tag.Tool], Effects: [], GoldValue: ToolValue));

        return (manager, catalog);
    }

    [TestMethod]
    public void CanTrade_AllowedTagsNull_AnyItemMatches()
    {
        var (_, catalog) = BuildManager();
        catalog.TryGet(ToolItemId, out var tool);

        Assert.IsTrue(ShopActions.CanTrade(GeneralShop, tool));
    }

    [TestMethod]
    public void CanTrade_ItemHasMatchingTag_ReturnsTrue()
    {
        var (_, catalog) = BuildManager();
        catalog.TryGet(PotionItemId, out var potion);

        Assert.IsTrue(ShopActions.CanTrade(PotionOnlyShop, potion));
    }

    [TestMethod]
    public void CanTrade_ItemHasNoMatchingTag_ReturnsFalse()
    {
        var (_, catalog) = BuildManager();
        catalog.TryGet(ToolItemId, out var tool);

        Assert.IsFalse(ShopActions.CanTrade(PotionOnlyShop, tool));
    }

    [TestMethod]
    public void ComputeBuyPrice_RoundsValueByBuyMultiplier()
    {
        var (_, catalog) = BuildManager();
        catalog.TryGet(PotionItemId, out var potion);

        // 10 * 1.10 = 11 exactly.
        Assert.AreEqual(11, ShopActions.ComputeBuyPrice(PotionOnlyShop, potion));
    }

    [TestMethod]
    public void ComputeSellPrice_RoundsValueBySellMultiplier()
    {
        var (_, catalog) = BuildManager();
        catalog.TryGet(PotionItemId, out var potion);

        // 10 * 0.90 = 9 exactly.
        Assert.AreEqual(9, ShopActions.ComputeSellPrice(PotionOnlyShop, potion));
    }

    [TestMethod]
    public void TryBuyFromShop_Success_MovesItemToPlayerAndGoldToShop()
    {
        var (manager, catalog) = BuildManager();
        manager.Merge(ShopEntityId, PotionOnlyShop);
        manager.Merge(ShopEntityId, new CurrencyComponent(gold: 0, credits: 0));
        manager.Merge(PlayerEntityId, new CurrencyComponent(gold: 100, credits: 0));
        var stackId = InventoryActions.AddItem(manager, ShopEntityId, PotionItemId, quantity: 3);

        var result = ShopActions.TryBuyFromShop(manager, catalog, PlayerEntityId, ShopEntityId, stackId, NoEntityIsThePlayer);

        Assert.IsTrue(result);
        var currencies = manager.GetPackedPool<CurrencyComponent>();
        Assert.AreEqual(100 - 11 * 3, currencies.GetReadonly(PlayerEntityId).Gold);
        Assert.AreEqual(11 * 3, currencies.GetReadonly(ShopEntityId).Gold);

        var stacks = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsFalse(stacks.Has(ShopEntityId));
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(stacks, PlayerEntityId, stackId, out var movedStack));
        Assert.AreEqual((ushort)3, movedStack.Quantity);
    }

    [TestMethod]
    public void TryBuyFromShop_WrongTag_ReturnsFalseAndChangesNothing()
    {
        var (manager, catalog) = BuildManager();
        manager.Merge(ShopEntityId, PotionOnlyShop);
        manager.Merge(ShopEntityId, new CurrencyComponent(gold: 0, credits: 0));
        manager.Merge(PlayerEntityId, new CurrencyComponent(gold: 100, credits: 0));
        var stackId = InventoryActions.AddItem(manager, ShopEntityId, ToolItemId, quantity: 1);

        var result = ShopActions.TryBuyFromShop(manager, catalog, PlayerEntityId, ShopEntityId, stackId, NoEntityIsThePlayer);

        Assert.IsFalse(result);
        Assert.AreEqual(100, manager.GetPackedPool<CurrencyComponent>().GetReadonly(PlayerEntityId).Gold);
        Assert.IsTrue(manager.GetMultiPool<InventoryItemStackComponent>().Has(ShopEntityId));
    }

    [TestMethod]
    public void TryBuyFromShop_PlayerCannotAfford_ReturnsFalseAndChangesNothing()
    {
        var (manager, catalog) = BuildManager();
        manager.Merge(ShopEntityId, PotionOnlyShop);
        manager.Merge(ShopEntityId, new CurrencyComponent(gold: 0, credits: 0));
        manager.Merge(PlayerEntityId, new CurrencyComponent(gold: 5, credits: 0));
        var stackId = InventoryActions.AddItem(manager, ShopEntityId, PotionItemId, quantity: 3);

        var result = ShopActions.TryBuyFromShop(manager, catalog, PlayerEntityId, ShopEntityId, stackId, NoEntityIsThePlayer);

        Assert.IsFalse(result);
        Assert.AreEqual(5, manager.GetPackedPool<CurrencyComponent>().GetReadonly(PlayerEntityId).Gold);
        Assert.AreEqual(0, manager.GetPackedPool<CurrencyComponent>().GetReadonly(ShopEntityId).Gold);
        Assert.IsTrue(manager.GetMultiPool<InventoryItemStackComponent>().Has(ShopEntityId));
    }

    [TestMethod]
    public void TryBuyFromShop_StackNotOnShop_ReturnsFalse()
    {
        var (manager, catalog) = BuildManager();
        manager.Merge(ShopEntityId, PotionOnlyShop);
        manager.Merge(ShopEntityId, new CurrencyComponent(gold: 0, credits: 0));
        manager.Merge(PlayerEntityId, new CurrencyComponent(gold: 100, credits: 0));

        var result = ShopActions.TryBuyFromShop(manager, catalog, PlayerEntityId, ShopEntityId, Guid.NewGuid(), NoEntityIsThePlayer);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryBuyFromShop_ShopEntityHasNoShopComponent_ReturnsFalse()
    {
        var (manager, catalog) = BuildManager();
        manager.Merge(PlayerEntityId, new CurrencyComponent(gold: 100, credits: 0));
        var stackId = InventoryActions.AddItem(manager, ShopEntityId, PotionItemId, quantity: 1);

        var result = ShopActions.TryBuyFromShop(manager, catalog, PlayerEntityId, ShopEntityId, stackId, NoEntityIsThePlayer);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TrySellToShop_Success_MovesItemToShopAndGoldToPlayer()
    {
        var (manager, catalog) = BuildManager();
        manager.Merge(ShopEntityId, PotionOnlyShop);
        manager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));
        manager.Merge(PlayerEntityId, new CurrencyComponent(gold: 0, credits: 0));
        var stackId = InventoryActions.AddItem(manager, PlayerEntityId, PotionItemId, quantity: 4);

        var result = ShopActions.TrySellToShop(manager, catalog, PlayerEntityId, ShopEntityId, stackId, NoEntityIsThePlayer);

        Assert.IsTrue(result);
        var currencies = manager.GetPackedPool<CurrencyComponent>();
        Assert.AreEqual(9 * 4, currencies.GetReadonly(PlayerEntityId).Gold);
        Assert.AreEqual(1000 - 9 * 4, currencies.GetReadonly(ShopEntityId).Gold);

        var stacks = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsFalse(stacks.Has(PlayerEntityId));
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(stacks, ShopEntityId, stackId, out var movedStack));
        Assert.AreEqual((ushort)4, movedStack.Quantity);
    }

    [TestMethod]
    public void TrySellToShop_ShopCannotAfford_ReturnsFalseAndChangesNothing()
    {
        var (manager, catalog) = BuildManager();
        manager.Merge(ShopEntityId, PotionOnlyShop);
        manager.Merge(ShopEntityId, new CurrencyComponent(gold: 5, credits: 0));
        manager.Merge(PlayerEntityId, new CurrencyComponent(gold: 0, credits: 0));
        var stackId = InventoryActions.AddItem(manager, PlayerEntityId, PotionItemId, quantity: 4);

        var result = ShopActions.TrySellToShop(manager, catalog, PlayerEntityId, ShopEntityId, stackId, NoEntityIsThePlayer);

        Assert.IsFalse(result);
        Assert.AreEqual(0, manager.GetPackedPool<CurrencyComponent>().GetReadonly(PlayerEntityId).Gold);
        Assert.AreEqual(5, manager.GetPackedPool<CurrencyComponent>().GetReadonly(ShopEntityId).Gold);
        Assert.IsTrue(manager.GetMultiPool<InventoryItemStackComponent>().Has(PlayerEntityId));
    }

    [TestMethod]
    public void TrySellToShop_WrongTag_ReturnsFalseAndChangesNothing()
    {
        var (manager, catalog) = BuildManager();
        manager.Merge(ShopEntityId, PotionOnlyShop);
        manager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));
        manager.Merge(PlayerEntityId, new CurrencyComponent(gold: 0, credits: 0));
        var stackId = InventoryActions.AddItem(manager, PlayerEntityId, ToolItemId, quantity: 1);

        var result = ShopActions.TrySellToShop(manager, catalog, PlayerEntityId, ShopEntityId, stackId, NoEntityIsThePlayer);

        Assert.IsFalse(result);
        Assert.AreEqual(0, manager.GetPackedPool<CurrencyComponent>().GetReadonly(PlayerEntityId).Gold);
        Assert.IsTrue(manager.GetMultiPool<InventoryItemStackComponent>().Has(PlayerEntityId));
    }
}
