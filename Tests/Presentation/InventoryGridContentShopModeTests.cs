using Engine.ECS.Components;
using Engine.Math;
using Game.Modules;
using Game.Modules.Currency.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops;
using Game.Modules.Shops.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;

namespace Tests.Presentation;

/// <summary>
/// Shop-mode coverage for InventoryGridContent -- toggling MapViewState.OpenShopEntityId switches
/// between plain InventoryItemStackCell and the wider, price-showing ShopItemStackCell (see
/// InventoryGridContent.ActiveCellSize/RebuildCells), and drives per-cell CompareState off shop
/// trade eligibility (tag match + the paying side's own Gold) rather than Item Details Comparison
/// while a shop is open (see UpdateShopEligibilityState).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class InventoryGridContentShopModeTests
{
    private const int PlayerEntityId = 1;
    private const int ShopEntityId = 2;

    private static readonly Guid PotionItemId = Guid.NewGuid();
    private static readonly Guid ToolItemId = Guid.NewGuid();
    private const int PotionValue = 10;
    private const int ToolValue = 20;

    private static (InventoryGridContent Grid, Window HostWindow, ComponentManager ComponentManager, MapViewState MapViewState) Build(int gridEntityId)
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 20);
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ShopComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<CurrencyComponent>(static (ref existing, incoming) => existing = incoming);

        var fontService = TestFonts.Shared;
        var labelRenderer = new LabelRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, labelRenderer);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<ShopItemStackCell>(() => new ShopItemStackCell(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(fontService, windowService, labelRenderer));

        var world = new Game.World.World(new Game.World.Map(new Vector3Int(10, 10, 1))) { PlayerEntityId = PlayerEntityId };
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(windowService, new UiLayerStack());
        var mapViewState = new MapViewState();

        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(PotionItemId, "Test Potion", null, "p", Color.White, Tags: [Tag.Potion], Effects: [], GoldValue: PotionValue));
        itemCatalog.Register(new ItemDefinition(ToolItemId, "Test Tool", null, "t", Color.White, Tags: [Tag.Tool], Effects: [], GoldValue: ToolValue));

        var hoverPopup = windowService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = new Vector2(220, 220), DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        hoverPopup.Initialize();

        var grid = new InventoryGridContent(world, componentManager, itemCatalog, windowService, fontService, labelRenderer, spriteSheetService, spriteRenderer, contextMenuController, gridEntityId, filterTag: null, hoverPopup, static () => null, mapViewState, static (_, _) => { }, static (_, _) => { });

        var hostWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(400, 200), DisplayMode = ElementDisplayMode.Fixed },
        });
        hostWindow.ContentPadding = Vector2.Zero;
        hostWindow.Initialize();
        grid.Initialize(hostWindow);

        return (grid, hostWindow, componentManager, mapViewState);
    }

    [TestMethod]
    public void OutsideShopMode_BuildsPlainInventoryItemStackCells()
    {
        var (grid, hostWindow, componentManager, _) = Build(PlayerEntityId);
        InventoryActions.AddItem(componentManager, PlayerEntityId, PotionItemId, quantity: 1);
        grid.Update(new GameTime());

        Assert.IsTrue(hostWindow.ChildElements.OfType<InventoryItemStackCell>().Any());
        Assert.IsFalse(hostWindow.ChildElements.OfType<ShopItemStackCell>().Any());
    }

    [TestMethod]
    public void OpeningShop_SwitchesGridToShopItemStackCells()
    {
        var (grid, hostWindow, componentManager, mapViewState) = Build(PlayerEntityId);
        InventoryActions.AddItem(componentManager, PlayerEntityId, PotionItemId, quantity: 3);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));

        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());

        var shopCell = hostWindow.ChildElements.OfType<ShopItemStackCell>().Single();
        // ShopItemStackCell IS an InventoryItemStackCell (subclass) -- every cell OfType<InventoryItemStackCell>() finds here must be the shop-mode subtype, not the plain base one.
        Assert.AreEqual(hostWindow.ChildElements.OfType<InventoryItemStackCell>().Count(), hostWindow.ChildElements.OfType<ShopItemStackCell>().Count());
        Assert.AreEqual(InventoryGridContent.CellSize.X * 4, shopCell.CurrentSize.X);
        Assert.AreEqual(InventoryGridContent.CellSize.Y, shopCell.CurrentSize.Y);
    }

    /// <summary>
    /// Regression test for a confirmed live bug: the player's starting kit and a shop's own random
    /// stock draw from the same item catalog, so buying an item the player already carries some of
    /// is the common case, not an edge one. InventoryActions.TryTransferStack never merges into an
    /// existing stack on the destination (see its own doc comment), so the player ends up with two
    /// separate physical stacks of the same item id -- which InventoryGridContent's own default
    /// GroupDivergedStacks merging would otherwise collapse into a single "Merged Stack" badge cell
    /// (StackInstanceId null), permanently unsellable (BuildItemContextMenu/UiInputController's
    /// drag path both require a real StackInstanceId) and always Ineligible (no single stack to
    /// price). Shop mode must force one cell per physical stack instead (see BuildCellEntries),
    /// so a freshly bought item that collides with an existing stack still renders as two
    /// independently priced, independently sellable cells.
    /// </summary>
    [TestMethod]
    public void BuyingItemAlreadyOwned_DoesNotMergeIntoAnUntradeableCell()
    {
        var (grid, hostWindow, componentManager, mapViewState) = Build(PlayerEntityId);
        InventoryActions.AddItem(componentManager, PlayerEntityId, PotionItemId, quantity: 5); // the player's own "starting kit" stack
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));

        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());
        Assert.HasCount(1, hostWindow.ChildElements.OfType<ShopItemStackCell>(), "Sanity check: a single starting stack, no collision yet.");

        // Simulate a purchase landing a second, separate stack of the SAME item under the player --
        // exactly what ShopActions.TryBuyFromShop's own InventoryActions.TryTransferStack call
        // does, without needing the full drag/UiInputController machinery here.
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 2);
        var shopStacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        InventoryQueries.TryGetStack(shopStacks, ShopEntityId, PotionItemId, out var boughtStack);
        InventoryActions.TryTransferStack(componentManager, ShopEntityId, PlayerEntityId, boughtStack.StackInstanceId, playerQuery: null);

        grid.Update(new GameTime());

        var playerCells = hostWindow.ChildElements.OfType<ShopItemStackCell>().ToList();
        Assert.HasCount(2, playerCells, "Two separate physical stacks must render as two separate cells, not one Merged Stack badge.");
        Assert.IsTrue(playerCells.All(cell => cell.StackInstanceId is not null), "Every shop-mode cell must have a real StackInstanceId -- a Merged Stack (null) can never be priced, given, taken, or dragged.");
        Assert.IsTrue(playerCells.All(cell => cell.CompareState == CellCompareState.Eligible), "Both stacks are Potion-tagged and the shop can easily afford to buy either back.");
    }

    [TestMethod]
    public void ClosingShop_RevertsGridToPlainCells()
    {
        var (grid, hostWindow, componentManager, mapViewState) = Build(PlayerEntityId);
        InventoryActions.AddItem(componentManager, PlayerEntityId, PotionItemId, quantity: 1);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: null, buyMultiplier: 1.2f, sellMultiplier: 0.8f));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));

        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());
        Assert.IsTrue(hostWindow.ChildElements.OfType<ShopItemStackCell>().Any());

        mapViewState.OpenShopEntityId = null;
        grid.Update(new GameTime());

        Assert.IsFalse(hostWindow.ChildElements.OfType<ShopItemStackCell>().Any());
        Assert.IsTrue(hostWindow.ChildElements.OfType<InventoryItemStackCell>().Any());
    }

    [TestMethod]
    public void PlayerGrid_ShopCannotAffordWrongTagItem_MarksCellIneligible()
    {
        var (grid, hostWindow, componentManager, mapViewState) = Build(PlayerEntityId);
        InventoryActions.AddItem(componentManager, PlayerEntityId, ToolItemId, quantity: 1);
        // Potion-only shop -- a Tool-tagged item can never be sold to it, regardless of Gold.
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));

        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<ShopItemStackCell>().Single();
        Assert.AreEqual(CellCompareState.Ineligible, cell.CompareState);
    }

    [TestMethod]
    public void PlayerGrid_ShopCanAffordMatchingTagItem_MarksCellEligible()
    {
        var (grid, hostWindow, componentManager, mapViewState) = Build(PlayerEntityId);
        InventoryActions.AddItem(componentManager, PlayerEntityId, PotionItemId, quantity: 2);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));

        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<ShopItemStackCell>().Single();
        Assert.AreEqual(CellCompareState.Eligible, cell.CompareState);
    }

    [TestMethod]
    public void ShopGrid_PlayerCannotAfford_MarksCellIneligible()
    {
        var (grid, hostWindow, componentManager, mapViewState) = Build(ShopEntityId);
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 5); // 5 * ceil(10*1.10)=11 => 55G, player can't afford
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 5, credits: 0));

        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<ShopItemStackCell>().Single();
        Assert.AreEqual(CellCompareState.Ineligible, cell.CompareState);
    }

    /// <summary>
    /// Regression test for a confirmed live bug: a just-purchased item landing in the player's own
    /// grid (a version-triggered RebuildCells, not the shop-mode-toggle one) rendered as disabled/
    /// Ineligible until some unrelated later Update happened to run UpdateCompareState again --
    /// RebuildCells itself never used to leave freshly-built cells with correct eligibility, only
    /// Update's own trailing call did, and Initialize (the very first RebuildCells, with no
    /// trailing UpdateCompareState of its own) never got that follow-up at all. Builds the grid
    /// with shop mode ALREADY active and the item ALREADY in place before Initialize ever runs --
    /// mirrors a shop window that opens showing stock it can already afford -- and asserts
    /// eligibility is correct immediately, with no Update call at all.
    /// </summary>
    [TestMethod]
    public void Initialize_ShopModeAlreadyActiveWithAffordableItem_CellIsEligibleWithNoUpdateCall()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 20);
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ShopComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<CurrencyComponent>(static (ref existing, incoming) => existing = incoming);

        var fontService = TestFonts.Shared;
        var labelRenderer = new LabelRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, labelRenderer);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<ShopItemStackCell>(() => new ShopItemStackCell(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(fontService, windowService, labelRenderer));

        var world = new Game.World.World(new Game.World.Map(new Vector3Int(10, 10, 1))) { PlayerEntityId = PlayerEntityId };
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(windowService, new UiLayerStack());

        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(PotionItemId, "Test Potion", null, "p", Color.White, Tags: [Tag.Potion], Effects: [], GoldValue: PotionValue));

        // Everything set up BEFORE Initialize -- item already in the player's inventory, shop
        // already able to afford it, and OpenShopEntityId already pointing at it -- the exact
        // "shop window opens showing stock the player can already afford" shape, not a rebuild
        // triggered later by an Update call.
        InventoryActions.AddItem(componentManager, PlayerEntityId, PotionItemId, quantity: 1);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));
        var mapViewState = new MapViewState { OpenShopEntityId = ShopEntityId };

        var hoverPopup = windowService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = new Vector2(220, 220), DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        hoverPopup.Initialize();

        var grid = new InventoryGridContent(world, componentManager, itemCatalog, windowService, fontService, labelRenderer, spriteSheetService, spriteRenderer, contextMenuController, PlayerEntityId, filterTag: null, hoverPopup, static () => null, mapViewState, static (_, _) => { }, static (_, _) => { });

        var hostWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(400, 200), DisplayMode = ElementDisplayMode.Fixed },
        });
        hostWindow.ContentPadding = Vector2.Zero;
        hostWindow.Initialize();

        grid.Initialize(hostWindow); // No grid.Update(...) call anywhere in this test.

        var cell = hostWindow.ChildElements.OfType<ShopItemStackCell>().Single();
        Assert.AreEqual(CellCompareState.Eligible, cell.CompareState);
    }

    [TestMethod]
    public void ShopGrid_PlayerCanAfford_MarksCellEligibleAndSetsPriceText()
    {
        var (grid, hostWindow, componentManager, mapViewState) = Build(ShopEntityId);
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 1);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 100, credits: 0));

        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<ShopItemStackCell>().Single();
        Assert.AreEqual(CellCompareState.Eligible, cell.CompareState);
    }
}
