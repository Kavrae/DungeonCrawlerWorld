using Engine.ECS.Components;
using Engine.Math;
using Game.Floors;
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
        componentManager.RegisterMultiPool<ShopStockPreferenceComponent>();
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
        componentManager.RegisterMultiPool<ShopStockPreferenceComponent>();
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

    [TestMethod]
    public void ShopGrid_StockBelowDefaultPreferredLevel_CellReadsUnderstocked()
    {
        var (grid, hostWindow, componentManager, mapViewState) = Build(ShopEntityId);
        // No ShopStockPreferenceComponent recorded -- falls back to ShopStockPricing.DefaultPreferredStockLevel
        // (20), whose 5-band edges are (10, 15, 25, 30); 12 sits in the Understocked band [10, 14].
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 12);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 100, credits: 0));

        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<ShopItemStackCell>().Single();
        Assert.AreEqual(StockStatus.Understocked, cell.StockStatus);
        // Understocked on the shop's own grid is a worse price to buy at -- unfavorable, not favorable.
        Assert.IsTrue(cell.IsThisGridTheShop);
        Assert.IsTrue(cell.PriceIsUnfavorable);
        Assert.IsFalse(cell.PriceIsFavorable);
    }

    [TestMethod]
    public void ShopGrid_StockFarBelowDefaultPreferredLevel_CellReadsDesperate()
    {
        var (grid, hostWindow, componentManager, mapViewState) = Build(ShopEntityId);
        // Default preferred level 20 -> edges (10, 15, 25, 30); 1 sits below the Desperate edge (10).
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 1);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 100, credits: 0));

        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<ShopItemStackCell>().Single();
        Assert.AreEqual(StockStatus.Desperate, cell.StockStatus);
        Assert.IsTrue(cell.PriceIsUnfavorable);
        Assert.IsFalse(cell.PriceIsFavorable);
    }

    [TestMethod]
    public void ShopGrid_StockAboveDefaultOverstockThreshold_CellReadsOverstocked()
    {
        var (grid, hostWindow, componentManager, mapViewState) = Build(ShopEntityId);
        // Default preferred level 20 -> edges (10, 15, 25, 30); 30 sits at the Overstocked band's own upper edge.
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 30);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 1000, credits: 0));

        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<ShopItemStackCell>().Single();
        Assert.AreEqual(StockStatus.Overstocked, cell.StockStatus);
        // Overstocked on the shop's own grid is a better price to buy at -- favorable, not unfavorable.
        Assert.IsTrue(cell.PriceIsFavorable);
        Assert.IsFalse(cell.PriceIsUnfavorable);
    }

    [TestMethod]
    public void ShopGrid_StockFarAboveDefaultOverstockThreshold_CellReadsFlooded()
    {
        var (grid, hostWindow, componentManager, mapViewState) = Build(ShopEntityId);
        // Default preferred level 20 -> edges (10, 15, 25, 30); 999 sits well past the Flooded edge (30).
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 999);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 10000, credits: 0));

        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<ShopItemStackCell>().Single();
        Assert.AreEqual(StockStatus.Flooded, cell.StockStatus);
        Assert.IsTrue(cell.PriceIsFavorable);
        Assert.IsFalse(cell.PriceIsUnfavorable);
    }

    [TestMethod]
    public void ShopGrid_StockWithinPreferredBand_CellReadsNormal()
    {
        var (grid, hostWindow, componentManager, mapViewState) = Build(ShopEntityId);
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 1);
        componentManager.GetMultiPool<ShopStockPreferenceComponent>().Add(ShopEntityId, new ShopStockPreferenceComponent(PotionItemId, preferredStockLevel: 1));
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 100, credits: 0));

        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<ShopItemStackCell>().Single();
        Assert.AreEqual(StockStatus.Normal, cell.StockStatus);
        Assert.IsFalse(cell.PriceIsFavorable);
        Assert.IsFalse(cell.PriceIsUnfavorable);
    }

    [TestMethod]
    public void PlayerGrid_ShopUnderstocked_CellReadsUnderstockedAndFavorable()
    {
        // StockStatus is always keyed off the shop's own stock, not whichever grid is rendering --
        // the player's own grid while shop mode is active must read the shop's Understocked status
        // too (see InventoryGridContent.ComputeShopStockStatus's own doc comment). Unlike the shop's
        // own grid, Understocked on the PLAYER's grid is a *better* price to sell at -- favorable.
        var (grid, hostWindow, componentManager, mapViewState) = Build(PlayerEntityId);
        InventoryActions.AddItem(componentManager, PlayerEntityId, PotionItemId, quantity: 1);
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 12); // shop itself is understocked (default preferred 20 -> edges 10/15/25/30)
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));

        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<ShopItemStackCell>().Single();
        Assert.AreEqual(StockStatus.Understocked, cell.StockStatus);
        Assert.IsFalse(cell.IsThisGridTheShop);
        Assert.IsTrue(cell.PriceIsFavorable);
        Assert.IsFalse(cell.PriceIsUnfavorable);
    }

    [TestMethod]
    public void PlayerGrid_ShopOverstocked_CellReadsOverstockedAndUnfavorable()
    {
        // The mirror image of PlayerGrid_ShopUnderstocked_CellReadsUnderstockedAndFavorable --
        // Overstocked is a *worse* price to sell at on the player's own grid, the opposite of what
        // it means on the shop's own grid.
        var (grid, hostWindow, componentManager, mapViewState) = Build(PlayerEntityId);
        InventoryActions.AddItem(componentManager, PlayerEntityId, PotionItemId, quantity: 1);
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 30); // shop itself is overstocked
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));

        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<ShopItemStackCell>().Single();
        Assert.AreEqual(StockStatus.Overstocked, cell.StockStatus);
        Assert.IsFalse(cell.PriceIsFavorable);
        Assert.IsTrue(cell.PriceIsUnfavorable);
    }

    /// <summary>
    /// Same shape as Build, but parameterized on getSecondaryTargetEntityId/tradeGridIsShopSide and
    /// returning the ContextMenuController/World too -- what BuildItemContextMenu's own "Sell All"/
    /// "Buy All"/"Add to trade" and the trade-grid right-click-removes-immediately tests below all
    /// need, none of which Build's own callers (secondary target always null, never a trade column)
    /// exercise.
    /// </summary>
    private static (InventoryGridContent Grid, Window HostWindow, ComponentManager ComponentManager, MapViewState MapViewState, ContextMenuController ContextMenuController, Game.World.World World) BuildForContextMenu(
        int gridEntityId, Func<int?> getSecondaryTargetEntityId, bool? tradeGridIsShopSide = null)
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 20);
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ShopComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<ShopStockPreferenceComponent>();
        componentManager.RegisterPackedPool<CurrencyComponent>(static (ref existing, incoming) => existing = incoming);

        var fontService = TestFonts.Shared;
        var labelRenderer = new LabelRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, labelRenderer);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<ShopItemStackCell>(() => new ShopItemStackCell(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<TradeItemStackCell>(() => new TradeItemStackCell(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<EmptyTradeSlotCell>(() => new EmptyTradeSlotCell(fontService, windowService, labelRenderer));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(fontService, windowService, labelRenderer));
        windowService.RegisterFactory<ContextMenu>(() => new ContextMenu(fontService, windowService, labelRenderer));

        var world = new Game.World.World(new Game.World.Map(new Vector3Int(10, 10, 1))) { PlayerEntityId = PlayerEntityId };
        var layers = new UiLayerStack();
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(windowService, layers);
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

        var grid = new InventoryGridContent(world, componentManager, itemCatalog, windowService, fontService, labelRenderer, spriteSheetService, spriteRenderer, contextMenuController, gridEntityId, filterTag: null, hoverPopup, getSecondaryTargetEntityId, mapViewState, static (_, _) => { }, static (_, _) => { }, tradeGridIsShopSide);

        var hostWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(400, 200), DisplayMode = ElementDisplayMode.Fixed },
        });
        hostWindow.ContentPadding = Vector2.Zero;
        hostWindow.Initialize();
        grid.Initialize(hostWindow);

        return (grid, hostWindow, componentManager, mapViewState, contextMenuController, world);
    }

    private static string[] MenuLabels(ContextMenuController contextMenuController) =>
        contextMenuController.Menu.ChildElements.OfType<Button>().Select(b => b.LeftText).ToArray();

    [TestMethod]
    public void RightClick_PlayerCellWithShopSecondaryTarget_ShowsSellAllNotGive()
    {
        var (grid, hostWindow, componentManager, mapViewState, contextMenuController, _) = BuildForContextMenu(PlayerEntityId, () => ShopEntityId);
        InventoryActions.AddItem(componentManager, PlayerEntityId, PotionItemId, quantity: 1);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));
        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<InventoryItemStackCell>().Single();
        cell.OnRightClicked!.Invoke(Point.Zero);

        Assert.IsTrue(contextMenuController.IsOpen);
        CollectionAssert.Contains(MenuLabels(contextMenuController), "Sell All");
        CollectionAssert.DoesNotContain(MenuLabels(contextMenuController), "Give");
    }

    [TestMethod]
    public void RightClick_ShopCellWithShopSecondaryTarget_ShowsBuyAllNotTake()
    {
        var (grid, hostWindow, componentManager, mapViewState, contextMenuController, _) = BuildForContextMenu(ShopEntityId, () => ShopEntityId);
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 1);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));
        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 1000, credits: 0));
        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<InventoryItemStackCell>().Single();
        cell.OnRightClicked!.Invoke(Point.Zero);

        Assert.IsTrue(contextMenuController.IsOpen);
        CollectionAssert.Contains(MenuLabels(contextMenuController), "Buy All");
        CollectionAssert.DoesNotContain(MenuLabels(contextMenuController), "Take");
    }

    [TestMethod]
    public void RightClick_PlayerCellWithNonShopSecondaryTarget_KeepsGiveLabel()
    {
        const int corpseEntityId = 99;
        var (grid, hostWindow, componentManager, _, contextMenuController, _) = BuildForContextMenu(PlayerEntityId, () => corpseEntityId);
        InventoryActions.AddItem(componentManager, PlayerEntityId, PotionItemId, quantity: 1);
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<InventoryItemStackCell>().Single();
        cell.OnRightClicked!.Invoke(Point.Zero);

        Assert.IsTrue(contextMenuController.IsOpen);
        CollectionAssert.Contains(MenuLabels(contextMenuController), "Give");
        CollectionAssert.DoesNotContain(MenuLabels(contextMenuController), "Sell All");
    }

    [TestMethod]
    public void RightClick_PlayerCellWhileShopOpen_OffersAddToTradeAndMovesStackThere()
    {
        const int tradePlayerEntityId = 50;
        var (grid, hostWindow, componentManager, mapViewState, contextMenuController, _) = BuildForContextMenu(PlayerEntityId, () => null);
        InventoryActions.AddItem(componentManager, PlayerEntityId, PotionItemId, quantity: 1);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));
        mapViewState.OpenShopEntityId = ShopEntityId;
        mapViewState.ReservedEntityIds = new ReservedEntityIds(tradePlayerEntityId, -1);
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<InventoryItemStackCell>().Single();
        cell.OnRightClicked!.Invoke(Point.Zero);

        Assert.IsTrue(contextMenuController.IsOpen);
        CollectionAssert.Contains(MenuLabels(contextMenuController), "Add to trade");

        var addToTradeButton = contextMenuController.Menu.ChildElements.OfType<Button>().Single(b => b.LeftText == "Add to trade");
        addToTradeButton.HandleClick(addToTradeButton.Rectangle.Center);

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsFalse(InventoryQueries.TryGetStack(stacks, PlayerEntityId, PotionItemId, out _));
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, tradePlayerEntityId, PotionItemId, out _));
    }

    /// <summary>
    /// PLAN-trade-window.md's own last open item on this: "Add to trade" was never explicitly
    /// capped, relying entirely on InventoryActions.TryTransferStack's own
    /// InventoryCapacity.HasRoomForNewStack check (the same 20-stack, non-player cap every other
    /// transfer destination already respects) -- this confirms that generic check actually holds
    /// for a trade-offer entity specifically, refusing a 21st stack rather than silently exceeding
    /// the grid's own fixed 20-slot layout (InventoryCapacity.MaxNonPlayerStackCount).
    /// </summary>
    [TestMethod]
    public void RightClick_AddToTrade_RefusesA21stStackOnceTradeColumnIsFull()
    {
        const int tradePlayerEntityId = 50;
        var (grid, hostWindow, componentManager, mapViewState, contextMenuController, _) = BuildForContextMenu(PlayerEntityId, () => null);
        InventoryActions.AddItem(componentManager, PlayerEntityId, PotionItemId, quantity: 1);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));
        mapViewState.OpenShopEntityId = ShopEntityId;
        mapViewState.ReservedEntityIds = new ReservedEntityIds(tradePlayerEntityId, -1);

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        for (var i = 0; i < InventoryCapacity.MaxNonPlayerStackCount; i++)
        {
            stacks.Add(tradePlayerEntityId, new InventoryItemStackComponent(PotionItemId, quantity: 1));
        }

        grid.Update(new GameTime());
        var cell = hostWindow.ChildElements.OfType<InventoryItemStackCell>().Single();
        cell.OnRightClicked!.Invoke(Point.Zero);

        Assert.IsTrue(contextMenuController.IsOpen);
        var addToTradeButton = contextMenuController.Menu.ChildElements.OfType<Button>().Single(b => b.LeftText == "Add to trade");
        addToTradeButton.HandleClick(addToTradeButton.Rectangle.Center);

        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, PlayerEntityId, PotionItemId, out _), "The item must still be in the player's own inventory -- the transfer must have been refused.");
        Assert.AreEqual(InventoryCapacity.MaxNonPlayerStackCount, stacks.CountForEntity(tradePlayerEntityId), "The trade column must still hold exactly its pre-seeded 20 stacks, not 21.");
    }

    [TestMethod]
    public void RightClick_ShopCellWhileShopOpen_OffersAddToTradeAndMovesStackThere()
    {
        const int tradeShopEntityId = 51;
        var (grid, hostWindow, componentManager, mapViewState, contextMenuController, _) = BuildForContextMenu(ShopEntityId, () => null);
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 1);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));
        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 1000, credits: 0));
        mapViewState.OpenShopEntityId = ShopEntityId;
        mapViewState.ReservedEntityIds = new ReservedEntityIds(-1, tradeShopEntityId);
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<InventoryItemStackCell>().Single();
        cell.OnRightClicked!.Invoke(Point.Zero);

        Assert.IsTrue(contextMenuController.IsOpen);
        CollectionAssert.Contains(MenuLabels(contextMenuController), "Add to trade");

        var addToTradeButton = contextMenuController.Menu.ChildElements.OfType<Button>().Single(b => b.LeftText == "Add to trade");
        addToTradeButton.HandleClick(addToTradeButton.Rectangle.Center);

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsFalse(InventoryQueries.TryGetStack(stacks, ShopEntityId, PotionItemId, out _));
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, tradeShopEntityId, PotionItemId, out _));
    }

    /// <summary>
    /// An unaffordable-but-right-tag item (0 Gold, Potion vs. a Potion-only shop) must still offer
    /// "Add to trade" (CanStageInTrade, tag match only -- see its own doc comment) but must NOT
    /// offer "Buy All" (gated by the stricter isShopIneligible, tag match AND affordability) --
    /// confirmed live requirement: an unaffordable item can still be staged for a trade/barter, just
    /// not bought outright.
    /// </summary>
    [TestMethod]
    public void RightClick_UnaffordableShopCell_OffersAddToTradeButNotBuyAll()
    {
        const int tradeShopEntityId = 51;
        var (grid, hostWindow, componentManager, mapViewState, contextMenuController, _) = BuildForContextMenu(ShopEntityId, () => ShopEntityId);
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 1);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 1000, credits: 0));
        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 0, credits: 0));
        mapViewState.OpenShopEntityId = ShopEntityId;
        mapViewState.ReservedEntityIds = new ReservedEntityIds(-1, tradeShopEntityId);
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<InventoryItemStackCell>().Single();
        Assert.AreEqual(CellCompareState.Ineligible, cell.CompareState, "Sanity check: 0 Gold can't afford this item.");
        Assert.IsTrue(cell.CanStageInTrade, "Sanity check: the item's own tag still matches the shop.");

        cell.OnRightClicked!.Invoke(Point.Zero);

        Assert.IsTrue(contextMenuController.IsOpen);
        CollectionAssert.Contains(MenuLabels(contextMenuController), "Add to trade");
        CollectionAssert.DoesNotContain(MenuLabels(contextMenuController), "Buy All");
    }

    [TestMethod]
    public void RightClick_TradePlayerColumnCell_RemovesFromTradeImmediatelyWithNoMenu()
    {
        const int tradePlayerEntityId = 50;
        var (grid, hostWindow, componentManager, _, contextMenuController, _) = BuildForContextMenu(tradePlayerEntityId, () => null, tradeGridIsShopSide: false);
        InventoryActions.AddItem(componentManager, tradePlayerEntityId, PotionItemId, quantity: 1);
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<InventoryItemStackCell>().Single();
        cell.OnRightClicked!.Invoke(Point.Zero);

        Assert.IsFalse(contextMenuController.IsOpen, "Trade-grid cells never open a context menu -- right-click removes the stack immediately instead.");
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsFalse(InventoryQueries.TryGetStack(stacks, tradePlayerEntityId, PotionItemId, out _));
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, PlayerEntityId, PotionItemId, out _));
    }

    [TestMethod]
    public void RightClick_TradeShopColumnCell_RemovesFromTradeImmediatelyWithNoMenu()
    {
        const int tradeShopEntityId = 51;
        var (grid, hostWindow, componentManager, mapViewState, contextMenuController, _) = BuildForContextMenu(tradeShopEntityId, () => null, tradeGridIsShopSide: true);
        InventoryActions.AddItem(componentManager, tradeShopEntityId, PotionItemId, quantity: 1);
        mapViewState.OpenShopEntityId = ShopEntityId;
        grid.Update(new GameTime());

        var cell = hostWindow.ChildElements.OfType<InventoryItemStackCell>().Single();
        cell.OnRightClicked!.Invoke(Point.Zero);

        Assert.IsFalse(contextMenuController.IsOpen, "Trade-grid cells never open a context menu -- right-click removes the stack immediately instead.");
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsFalse(InventoryQueries.TryGetStack(stacks, tradeShopEntityId, PotionItemId, out _));
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, ShopEntityId, PotionItemId, out _));
    }

    [TestMethod]
    public void TradeGrid_Empty_FillsAllSlotsWithEmptySlotCells()
    {
        const int tradePlayerEntityId = 50;
        var (grid, hostWindow, _, _, _, _) = BuildForContextMenu(tradePlayerEntityId, () => null, tradeGridIsShopSide: false);

        grid.Update(new GameTime());

        Assert.AreEqual(InventoryCapacity.MaxNonPlayerStackCount, hostWindow.ChildElements.OfType<EmptyTradeSlotCell>().Count());
        Assert.IsFalse(hostWindow.ChildElements.OfType<InventoryItemStackCell>().Any(), "An empty grid has no real cells at all -- InventoryItemStackCell also matches TradeItemStackCell (its subclass), so this alone rules out both.");
    }

    /// <summary>Confirms the fill count tracks *displayed* cells, not raw physical stacks -- two distinct items here produce exactly two real cells (no merging, since they're different ItemDefinitionIds), so the count formula (capacity minus displayed cells) generalizes beyond the all-empty case above.</summary>
    [TestMethod]
    public void TradeGrid_SomeStacks_FillsOnlyTheRemainderWithEmptySlotCells()
    {
        const int tradeShopEntityId = 51;
        var (grid, hostWindow, componentManager, mapViewState, _, _) = BuildForContextMenu(tradeShopEntityId, () => null, tradeGridIsShopSide: true);
        mapViewState.OpenShopEntityId = ShopEntityId;
        InventoryActions.AddItem(componentManager, tradeShopEntityId, PotionItemId, quantity: 1);
        InventoryActions.AddItem(componentManager, tradeShopEntityId, ToolItemId, quantity: 1);

        grid.Update(new GameTime());

        Assert.AreEqual(2, hostWindow.ChildElements.OfType<InventoryItemStackCell>().Count(), "Sanity check: two distinct items, no merging.");
        Assert.AreEqual(InventoryCapacity.MaxNonPlayerStackCount - 2, hostWindow.ChildElements.OfType<EmptyTradeSlotCell>().Count());
        Assert.AreEqual(2, grid.VisibleItemCount, "Decoration cells must never count as real, visible items.");
    }

    /// <summary>Only the trade window's own two columns get this treatment -- an ordinary shop-mode grid (the real shop's own, or the player's own while shop mode is active) never fills its unused space with decoration, empty or not.</summary>
    [TestMethod]
    public void NonTradeGrid_Empty_NeverGetsEmptySlotCells()
    {
        var (grid, hostWindow, _, mapViewState) = Build(PlayerEntityId);
        mapViewState.OpenShopEntityId = ShopEntityId;

        grid.Update(new GameTime());

        Assert.IsFalse(hostWindow.ChildElements.OfType<EmptyTradeSlotCell>().Any());
    }

    /// <summary>
    /// Regression test for a confirmed bug fixed this session: with part of an item's stock staged
    /// in the trade window's own shop-side column (MapViewState.ReservedEntityIds.TradeOfferShopEntityId), the price
    /// this grid shows for the *remaining* physical stack on the real shop's own grid used to be
    /// computed against the trade-inclusive "effective" stock (more stock -> a cheaper buy band for
    /// the same quantity), while ShopActions.TryBuyFromShop -- what actually charges Gold for a
    /// direct purchase -- only ever reads the shop's plain physical stock. See
    /// InventoryGridContent.EffectiveStockForThisGrid's own doc comment for why the real shop's own
    /// grid must price off the same number TryBuyFromShop does. Sets up its own World/ItemCatalog
    /// (Build's own helper doesn't expose either) the same way Initialize_ShopModeAlreadyActiveWith
    /// AffordableItem_CellIsEligibleWithNoUpdateCall above already does.
    /// </summary>
    [TestMethod]
    public void ShopGridPrice_MatchesActualChargeFromTryBuyFromShop_EvenWithStockStagedInTradeWindow()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 20);
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ShopComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<ShopStockPreferenceComponent>();
        componentManager.RegisterPackedPool<CurrencyComponent>(static (ref existing, incoming) => existing = incoming);

        var fontService = TestFonts.Shared;
        var labelRenderer = new LabelRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, labelRenderer);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        windowService.RegisterFactory<ShopItemStackCell>(() => new ShopItemStackCell(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(fontService, windowService, labelRenderer));

        var world = new Game.World.World(new Game.World.Map(new Vector3Int(10, 10, 1))) { PlayerEntityId = PlayerEntityId };
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(windowService, new UiLayerStack());
        var mapViewState = new MapViewState();

        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(PotionItemId, "Test Potion", null, "p", Color.White, Tags: [Tag.Potion], Effects: [], GoldValue: PotionValue));

        var hoverPopup = windowService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = new Vector2(220, 220), DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        hoverPopup.Initialize();

        var grid = new InventoryGridContent(world, componentManager, itemCatalog, windowService, fontService, labelRenderer, spriteSheetService, spriteRenderer, contextMenuController, ShopEntityId, filterTag: null, hoverPopup, static () => null, mapViewState, static (_, _) => { }, static (_, _) => { });

        var hostWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(400, 200), DisplayMode = ElementDisplayMode.Fixed },
        });
        hostWindow.ContentPadding = Vector2.Zero;
        hostWindow.Initialize();
        grid.Initialize(hostWindow);

        const int tradeShopEntityId = 99;
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 5); // still physically on the shop
        InventoryActions.AddItem(componentManager, tradeShopEntityId, PotionItemId, quantity: 5); // staged in the trade window's shop-side column

        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 0, credits: 0));
        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 1000, credits: 0));

        mapViewState.OpenShopEntityId = ShopEntityId;
        mapViewState.ReservedEntityIds = new ReservedEntityIds(-1, tradeShopEntityId);
        grid.Update(new GameTime());

        var shopCell = hostWindow.ChildElements.OfType<ShopItemStackCell>().Single();
        var displayedPrice = shopCell.TotalPrice;
        var stackInstanceId = shopCell.StackInstanceId!.Value;

        var goldBefore = componentManager.GetPackedPool<CurrencyComponent>().GetReadonly(PlayerEntityId).Gold;
        Assert.IsTrue(ShopActions.TryBuyFromShop(componentManager, itemCatalog, PlayerEntityId, ShopEntityId, stackInstanceId, world));
        var goldAfter = componentManager.GetPackedPool<CurrencyComponent>().GetReadonly(PlayerEntityId).Gold;

        Assert.AreEqual(displayedPrice, goldBefore - goldAfter, "The price the real shop grid displayed must equal what TryBuyFromShop actually charged -- staged trade stock must not change the real grid's own price.");
    }
}
