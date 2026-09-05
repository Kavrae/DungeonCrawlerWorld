using Engine.ECS.Components;
using Engine.Events;
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
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Input;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;
using Presentation.UI.Trade;

namespace Tests.Presentation;

/// <summary>
/// Covers TradeWindow.ComputeColumnValueText -- the live "Player Value"/"Shop Value" header
/// computation (PLAN-trade-window.md's own "Header: Player Value / Shop Value" section), the first
/// TradeWindow test coverage of any kind (its own drag-eligibility/context-menu behavior is covered
/// indirectly through InventoryGridContent/UiInputController instead, since that's where the actual
/// logic lives -- this file is specifically about the Value computation TradeWindow itself owns).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TradeWindowTests
{
    private const int PlayerEntityId = 1;
    private const int ShopEntityId = 2;
    private const int TradePlayerEntityId = 3;
    private const int TradeShopEntityId = 4;

    private static readonly Guid PotionItemId = Guid.NewGuid();

    /// <summary>A second, independent item -- only used by tests that need two sides' Values to come from items priced against *separate* stock counts (GetEffectiveShopStock sums per itemDefinitionId, so reusing PotionItemId on both sides would have each side's own stock count bleed into the other's price).</summary>
    private static readonly Guid GadgetItemId = Guid.NewGuid();

    /// <summary>PreferredStockLevel 50 keeps every stock level this file's tests touch (well under 25, the Desperate/Understocked boundary -- see ShopStockPricingTests' own precedent) inside a single, easy-to-hand-verify band, avoiding the preferredStockLevel-0 "any stock reads as Flooded" edge case UiInputControllerTests' own trade harness deliberately exercises instead.</summary>
    private const byte PreferredStockLevel = 50;

    private static (TradeWindow Window, ComponentManager ComponentManager, MapViewState MapViewState) Build(EventBus? eventBus = null)
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 20);
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ShopComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<ShopStockPreferenceComponent>();
        componentManager.RegisterPackedPool<CurrencyComponent>(static (ref existing, incoming) => existing = incoming);

        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.10f, sellMultiplier: 0.90f));
        componentManager.GetMultiPool<ShopStockPreferenceComponent>().Add(ShopEntityId, new ShopStockPreferenceComponent(PotionItemId, PreferredStockLevel));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 0, credits: 0));
        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 0, credits: 0));
        componentManager.Merge(TradePlayerEntityId, new CurrencyComponent(gold: 0, credits: 0));
        componentManager.Merge(TradeShopEntityId, new CurrencyComponent(gold: 0, credits: 0));

        var fontService = TestFonts.Shared;
        var labelRenderer = new LabelRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, labelRenderer);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        windowService.RegisterFactory<TradeItemStackCell>(() => new TradeItemStackCell(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<EmptyTradeSlotCell>(() => new EmptyTradeSlotCell(fontService, windowService, labelRenderer));
        windowService.RegisterFactory<CurrencyElement>(() => new CurrencyElement(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(fontService, windowService, labelRenderer));

        var world = new Game.World.World(new Game.World.Map(new Vector3Int(10, 10, 1))) { PlayerEntityId = PlayerEntityId };
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(windowService, new UiLayerStack());
        var mapViewState = new MapViewState { OpenShopEntityId = ShopEntityId, ReservedEntityIds = new ReservedEntityIds(TradePlayerEntityId, TradeShopEntityId) };

        componentManager.GetMultiPool<ShopStockPreferenceComponent>().Add(ShopEntityId, new ShopStockPreferenceComponent(GadgetItemId, PreferredStockLevel));

        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(PotionItemId, "Test Potion", null, "p", Color.White, Tags: [Tag.Potion], Effects: [], GoldValue: 10));
        itemCatalog.Register(new ItemDefinition(GadgetItemId, "Test Gadget", null, "g", Color.White, Tags: [], Effects: [], GoldValue: 10));

        windowService.RegisterFactory<TradeWindow>(() => new TradeWindow(fontService, windowService, labelRenderer, componentManager, itemCatalog, spriteSheetService, spriteRenderer, world, contextMenuController, mapViewState, eventBus));

        var playerPopup = windowService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = new Vector2(220, 220), DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        playerPopup.Initialize();
        var shopPopup = windowService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = new Vector2(220, 220), DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        shopPopup.Initialize();

        var window = windowService.CreateElement<TradeWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "Trade", ShowBorder = true, CanUserClose = true, CanUserMove = true, CanUserResize = false, CanUserFocus = true },
        });
        window.Configure(TradePlayerEntityId, TradeShopEntityId, ShopEntityId, playerPopup, shopPopup);
        window.Initialize();

        return (window, componentManager, mapViewState);
    }

    [TestMethod]
    public void Update_EmptyTrade_BothValuesReadZero()
    {
        var (window, _, _) = Build();

        window.Update(new GameTime());

        Assert.AreEqual("0G", window.PlayerValueText);
        Assert.AreEqual("0G", window.ShopValueText);
    }

    /// <summary>
    /// _shopEntityId is captured once at Configure time, not re-read from
    /// MapViewState.OpenShopEntityId later (see TradeWindow's own doc comment on that field --
    /// ShopWindowController already clears OpenShopEntityId back to null *before* the close
    /// cascade that eventually asks this window to unwind, so re-reading it at that point would
    /// silently strand whatever's staged in either trade-offer entity). Value computation's own
    /// fallback is therefore keyed on the shop's ShopComponent itself being resolvable, not on
    /// OpenShopEntityId -- this covers the one way that can still fail: the shop entity's own
    /// ShopComponent going away entirely (e.g. destroyed mid-trade).
    /// </summary>
    [TestMethod]
    public void Update_ShopComponentMissing_BothValuesReadZeroRegardlessOfContents()
    {
        var (window, componentManager, _) = Build();
        InventoryActions.AddItem(componentManager, TradePlayerEntityId, PotionItemId, quantity: 5);
        componentManager.GetPackedPool<ShopComponent>().Remove(ShopEntityId);

        window.Update(new GameTime());

        Assert.AreEqual("0G", window.PlayerValueText);
        Assert.AreEqual("0G", window.ShopValueText);
    }

    [TestMethod]
    public void Update_PlayerColumnStack_ValuesAtSellPricePlusFooterGold()
    {
        var (window, componentManager, _) = Build();
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 50);
        InventoryActions.AddItem(componentManager, TradePlayerEntityId, PotionItemId, quantity: 10);
        componentManager.Merge(TradePlayerEntityId, new CurrencyComponent(gold: 7, credits: 0));

        window.Update(new GameTime());

        // Pre-seeding the real shop with 50 (== PreferredStockLevel) keeps the 10-unit sell walk
        // (stock 50-59) inside Normal ([37,63] for preferred 50 -- edges 25/37/63/75), a flat
        // 10 GoldValue * 0.90 SellMultiplier = 9G/unit -- matches ShopStockPricingTests' own
        // ComputeBulkBuyPrice_SingleUnitInsideNormalBand_MatchesFlatShopPrice precedent. Selling
        // from the trade-player column never touches the real shop's own stock count (only the
        // shop-side column does, see GetEffectiveShopStock), so this 50 is purely a pricing anchor.
        Assert.AreEqual("97G", window.PlayerValueText, "10 units * 9G + 7G footer Gold = 97G.");
        Assert.AreEqual("0G", window.ShopValueText);
    }

    [TestMethod]
    public void Update_ShopColumnStack_ValuesAtBuyPricePlusFooterGold()
    {
        var (window, componentManager, _) = Build();
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 50);
        InventoryActions.AddItem(componentManager, TradeShopEntityId, PotionItemId, quantity: 3);
        componentManager.Merge(TradeShopEntityId, new CurrencyComponent(gold: 5, credits: 0));

        window.Update(new GameTime());

        // Effective stock (real shop's 50 + trade-shop column's own 3) = 53, still inside Normal --
        // flat 10 GoldValue * 1.10 BuyMultiplier = 11G/unit. Confirms GetEffectiveShopStock's own
        // correction is wired in here too, not just InventoryGridContent's cells -- without it,
        // this would price against the real shop's bare 50 (still Normal here, so wouldn't actually
        // catch a regression on its own) rather than 53.
        Assert.AreEqual("38G", window.ShopValueText, "3 units * 11G + 5G footer Gold = 38G.");
        Assert.AreEqual("0G", window.PlayerValueText);
    }

    /// <summary>
    /// Regression test for the "merged-stack-cell gotcha" (PLAN-trade-window.md's own Confirmed
    /// decisions) -- two separate 5-unit stacks of the same item must combine into one 10-unit bulk-
    /// price call, not two independent 5-unit calls, since bulk pricing is a non-linear per-band
    /// curve. Real shop stock seeded to 33 (Understocked, edges 25/37/63/75) specifically so the
    /// combined 10-unit sell walk (33-42) crosses into Normal at 37 -- a buggy per-stack-independent
    /// implementation would price both 5-unit halves from the *same* stock-33 starting point instead
    /// of advancing cumulatively between them, double-counting the first half's Understocked rate
    /// rather than ever pricing the second half's Normal-rate units, giving a different (and wrong)
    /// total from the correctly grouped one.
    /// </summary>
    [TestMethod]
    public void Update_TwoSeparateStacksOfSameItem_CombinesQuantitiesBeforeOneBulkPriceCall()
    {
        var (twoStackWindow, twoStackManager, _) = Build();
        InventoryActions.AddItem(twoStackManager, ShopEntityId, PotionItemId, quantity: 33);
        InventoryActions.AddItem(twoStackManager, TradePlayerEntityId, PotionItemId, quantity: 5);
        // A second, independent stack of the identical item -- AddItem itself would merge these,
        // so add directly to the pool to keep them physically separate, the same "two real stacks,
        // not one" shape a shop-stock collision naturally produces (see
        // InventoryGridContentShopModeTests' own BuyingItemAlreadyOwned_... precedent).
        twoStackManager.GetMultiPool<InventoryItemStackComponent>().Add(TradePlayerEntityId, new InventoryItemStackComponent(PotionItemId, quantity: 5));
        twoStackWindow.Update(new GameTime());

        var (oneStackWindow, oneStackManager, _) = Build();
        InventoryActions.AddItem(oneStackManager, ShopEntityId, PotionItemId, quantity: 33);
        InventoryActions.AddItem(oneStackManager, TradePlayerEntityId, PotionItemId, quantity: 10);
        oneStackWindow.Update(new GameTime());

        Assert.AreEqual(oneStackWindow.PlayerValueText, twoStackWindow.PlayerValueText);
    }

    private static Button FindButton(TradeWindow window, string text) =>
        window.ChildElements.OfType<Button>().Single(b => b.LeftText == text);

    private static void ClickButton(Button button) => button.HandleClick(button.Rectangle.Center);

    private static int ReadGold(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<CurrencyComponent>().GetReadonly(entityId).Gold;

    /// <summary>
    /// The user's own explicit correction over an earlier version that only netted out
    /// min(playerColumnGold, shopColumnGold): Balance Offer must remove *all* Gold from both trade
    /// columns first -- not just the smaller of the two -- returning each amount to its own real
    /// owner, so both columns' Values are reduced to their item contents alone before any
    /// rebalancing happens. With no items on either side here, both Values land at 0 after the
    /// wipe, so no rebalancing Gold is needed at all -- confirming the wipe itself, independent of
    /// the top-up step (which has its own dedicated tests below).
    /// </summary>
    [TestMethod]
    public void BalanceOffer_RemovesAllGoldFromBothSides_BeforeRebalancing()
    {
        var (window, componentManager, _) = Build();
        componentManager.Merge(TradePlayerEntityId, new CurrencyComponent(gold: 20, credits: 0));
        componentManager.Merge(TradeShopEntityId, new CurrencyComponent(gold: 15, credits: 0));
        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 100, credits: 0));
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 100, credits: 0));
        window.Update(new GameTime());

        ClickButton(FindButton(window, "Balance Offer"));

        // All Gold returns to its real owner: player column 20 -> 0 (real player 100 -> 120), shop
        // column 15 -> 0 (real shop 100 -> 115). Both Values are now 0 (no items on either side),
        // so no rebalancing Gold moves afterward. Conserved throughout (235 both before and after).
        Assert.AreEqual(0, ReadGold(componentManager, TradePlayerEntityId));
        Assert.AreEqual(0, ReadGold(componentManager, TradeShopEntityId));
        Assert.AreEqual(120, ReadGold(componentManager, PlayerEntityId));
        Assert.AreEqual(115, ReadGold(componentManager, ShopEntityId));
    }

    /// <summary>
    /// Matches PLAN-trade-window.md's own "Worked example" (Player Value 90G, Shop Value 66G, shop
    /// has >=24G) -- both Values come entirely from priced items (10 potions sold at 9G/unit, 6
    /// gadgets bought at 11G/unit, both Normal band off their own separately-anchored 50-unit real
    /// shop stock), not footer Gold, so the "remove all Gold first" wipe has nothing to remove and
    /// this test isolates the top-up mechanism alone.
    /// </summary>
    [TestMethod]
    public void BalanceOffer_ToppedUpFromShopWhenPlayerValueHigher_MatchesWorkedExample()
    {
        var (window, componentManager, _) = Build();
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 50);
        InventoryActions.AddItem(componentManager, TradePlayerEntityId, PotionItemId, quantity: 10);
        InventoryActions.AddItem(componentManager, ShopEntityId, GadgetItemId, quantity: 50);
        InventoryActions.AddItem(componentManager, TradeShopEntityId, GadgetItemId, quantity: 6);
        componentManager.Merge(ShopEntityId, new CurrencyComponent(gold: 24, credits: 0));
        window.Update(new GameTime());
        Assert.AreEqual("90G", window.PlayerValueText, "Sanity check: 10 potions * 9G (Normal band, effective stock 50-59) = 90G.");
        Assert.AreEqual("66G", window.ShopValueText, "Sanity check: 6 gadgets * 11G (Normal band, effective stock 56) = 66G.");

        ClickButton(FindButton(window, "Balance Offer"));

        Assert.AreEqual(0, ReadGold(componentManager, TradePlayerEntityId), "Nothing to wipe (no footer Gold there to begin with), and no rebalancing lands here either -- Player Value already came entirely from items.");
        Assert.AreEqual(24, ReadGold(componentManager, TradeShopEntityId), "0 (nothing to wipe) + 24 topped up.");
        Assert.AreEqual(0, ReadGold(componentManager, ShopEntityId), "The shop's real balance is now fully spent reaching equality.");

        window.Update(new GameTime());
        Assert.AreEqual("90G", window.ShopValueText, "66G item value + 24G footer Gold now tops up to match Player Value exactly.");
    }

    /// <summary>
    /// "until the two are equal or the [payer] runs out of currency" -- Shop Value (90G, from an
    /// item) here exceeds Player Value, and the real player has less than the deficit, so Balance
    /// Offer adds everything the player has and stops, leaving Shop Value still greater (unfavorable
    /// to the shop -- Complete stays disabled). The mirror image of the "shop pays" worked-example
    /// test above -- here it's the *player's* side that's short and gets topped up from the
    /// player's own real Gold.
    /// </summary>
    [TestMethod]
    public void BalanceOffer_PayerCannotFullyCoverDeficit_AddsWhatItHasAndStops()
    {
        var (window, componentManager, _) = Build();
        InventoryActions.AddItem(componentManager, ShopEntityId, GadgetItemId, quantity: 50);
        InventoryActions.AddItem(componentManager, TradeShopEntityId, GadgetItemId, quantity: 6);
        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 24, credits: 0));
        window.Update(new GameTime());
        Assert.AreEqual("0G", window.PlayerValueText);
        Assert.AreEqual("66G", window.ShopValueText, "Sanity check: 6 gadgets * 11G (Normal band, effective stock 56) = 66G.");

        ClickButton(FindButton(window, "Balance Offer"));

        Assert.AreEqual(24, ReadGold(componentManager, TradePlayerEntityId), "The player only had 24 of the 66 owed -- Balance Offer adds all of it.");
        Assert.AreEqual(0, ReadGold(componentManager, PlayerEntityId));

        window.Update(new GameTime());
        Assert.IsFalse(FindButton(window, "Complete").Enabled, "Still unequal (Player Value 24 < Shop Value 66, unfavorable to the shop) -- Complete must stay disabled.");
    }

    [TestMethod]
    public void CompleteButton_EnabledOnlyWhenNonEmptyAndPlayerValueAtLeastShopValue()
    {
        var (window, componentManager, _) = Build();
        window.Update(new GameTime());
        Assert.IsFalse(FindButton(window, "Complete").Enabled, "An empty trade has nothing to complete.");

        componentManager.Merge(TradePlayerEntityId, new CurrencyComponent(gold: 30, credits: 0));
        componentManager.Merge(TradeShopEntityId, new CurrencyComponent(gold: 50, credits: 0));
        window.Update(new GameTime());
        Assert.IsFalse(FindButton(window, "Complete").Enabled, "Shop Value (50) > Player Value (30) -- unfavorable to the shop, must stay disabled.");

        componentManager.Merge(TradePlayerEntityId, new CurrencyComponent(gold: 50, credits: 0));
        window.Update(new GameTime());
        Assert.IsTrue(FindButton(window, "Complete").Enabled, "Exactly equal (50 == 50) -- must enable.");

        componentManager.Merge(TradePlayerEntityId, new CurrencyComponent(gold: 60, credits: 0));
        window.Update(new GameTime());
        Assert.IsTrue(FindButton(window, "Complete").Enabled, "Favorable to the shop (60 > 50) -- the >= gate, not ==, must still enable.");
    }

    [TestMethod]
    public void Complete_SwapsItemsAndCurrencyBetweenColumnsAndTheRealEntities()
    {
        var (window, componentManager, _) = Build();
        InventoryActions.AddItem(componentManager, ShopEntityId, PotionItemId, quantity: 50);
        InventoryActions.AddItem(componentManager, TradePlayerEntityId, PotionItemId, quantity: 1);
        componentManager.Merge(TradePlayerEntityId, new CurrencyComponent(gold: 50, credits: 0));
        componentManager.Merge(TradeShopEntityId, new CurrencyComponent(gold: 30, credits: 0));
        window.Update(new GameTime());
        Assert.IsTrue(FindButton(window, "Complete").Enabled, "Sanity check: Player Value (50G + priced potion) must already exceed Shop Value (30G) here.");

        ClickButton(FindButton(window, "Complete"));

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsFalse(InventoryQueries.TryGetStack(stacks, TradePlayerEntityId, PotionItemId, out _), "The player-side item must have left the trade column entirely.");
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, ShopEntityId, PotionItemId, out _), "-- landing on the real shop, per PLAN-trade-window.md's own swap direction.");
        Assert.AreEqual(0, ReadGold(componentManager, TradePlayerEntityId));
        Assert.AreEqual(0, ReadGold(componentManager, TradeShopEntityId));
        Assert.AreEqual(50, ReadGold(componentManager, ShopEntityId), "The left footer's whole Gold balance moved to the real shop.");
        Assert.AreEqual(30, ReadGold(componentManager, PlayerEntityId), "The right footer's whole Gold balance moved to the real player.");
    }

    /// <summary>
    /// Confirmed live gap this closes: completing a trade that offers only player Gold (no items on
    /// either side) never published GoldGivenToShopEvent before CompleteTrade's player-to-shop Gold
    /// leg was routed through ShopActions.TryGiveCurrencyToShop -- see that method's own doc comment.
    /// </summary>
    [TestMethod]
    public void Complete_TradeWithOnlyPlayerCurrency_PublishesGoldGivenToShopEvent()
    {
        var eventBus = new EventBus();
        var (window, componentManager, _) = Build(eventBus);
        componentManager.Merge(TradePlayerEntityId, new CurrencyComponent(gold: 40, credits: 0));
        window.Update(new GameTime());
        Assert.IsTrue(FindButton(window, "Complete").Enabled, "Sanity check: Player Value (40G, no items) already exceeds Shop Value (0).");

        GoldGivenToShopEvent? published = null;
        eventBus.Subscribe<GoldGivenToShopEvent>(e => published = e);

        ClickButton(FindButton(window, "Complete"));

        Assert.AreEqual(40, ReadGold(componentManager, ShopEntityId));
        Assert.IsNotNull(published);
        Assert.AreEqual(PlayerEntityId, published!.Value.PlayerEntityId);
        Assert.AreEqual(ShopEntityId, published.Value.ShopEntityId);
        Assert.AreEqual(40, published.Value.Amount);
    }

    [TestMethod]
    public void ReturnEverythingToOwners_ReturnsEveryItemAndEveryCurrencyBalanceToItsRealOwner()
    {
        var (window, componentManager, _) = Build();
        InventoryActions.AddItem(componentManager, TradePlayerEntityId, PotionItemId, quantity: 4);
        InventoryActions.AddItem(componentManager, TradeShopEntityId, PotionItemId, quantity: 2);
        componentManager.Merge(TradePlayerEntityId, new CurrencyComponent(gold: 12, credits: 0));
        componentManager.Merge(TradeShopEntityId, new CurrencyComponent(gold: 8, credits: 0));

        window.ReturnEverythingToOwners();

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, PlayerEntityId, PotionItemId, out var playerStack) && playerStack.Quantity == 4);
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, ShopEntityId, PotionItemId, out var shopStack) && shopStack.Quantity == 2);
        Assert.AreEqual(0, ReadGold(componentManager, TradePlayerEntityId));
        Assert.AreEqual(0, ReadGold(componentManager, TradeShopEntityId));
        Assert.AreEqual(12, ReadGold(componentManager, PlayerEntityId));
        Assert.AreEqual(8, ReadGold(componentManager, ShopEntityId));
    }

    /// <summary>
    /// Regression test for the exact bug this session's own capture-_shopEntityId-at-Configure-time
    /// fix exists for: ShopWindowController.HandleClosed already clears MapViewState.OpenShopEntityId
    /// back to null *before* invoking TradeWindowController.CloseForShopClosed, which is what
    /// eventually calls this method -- if ReturnEverythingToOwners still read OpenShopEntityId at
    /// that point (instead of the captured _shopEntityId), this would have silently no-op'd,
    /// permanently stranding the item in the trade-shop column.
    /// </summary>
    [TestMethod]
    public void ReturnEverythingToOwners_StillWorks_AfterOpenShopEntityIdHasAlreadyBeenClearedToNull()
    {
        var (window, componentManager, mapViewState) = Build();
        InventoryActions.AddItem(componentManager, TradeShopEntityId, PotionItemId, quantity: 2);
        mapViewState.OpenShopEntityId = null;

        window.ReturnEverythingToOwners();

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, ShopEntityId, PotionItemId, out var shopStack) && shopStack.Quantity == 2, "Must still land on the real shop even though OpenShopEntityId reads null by the time this runs.");
    }

    private static MouseState MouseAt(int x, int y, ButtonState leftButton) =>
        new(x, y, 0, leftButton, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    [TestMethod]
    public void ResolveItemDropEntityId_LeftHalf_ReturnsPlayerSideEntity()
    {
        var (window, _, _) = Build();
        var leftPoint = new Point((int)window.ContentAbsolutePosition.X + 1, (int)window.ContentAbsolutePosition.Y + 1);

        Assert.AreEqual(TradePlayerEntityId, window.ResolveItemDropEntityId(leftPoint));
        Assert.AreEqual(TradePlayerEntityId, window.ResolveCurrencyDropEntityId(leftPoint));
    }

    [TestMethod]
    public void ResolveItemDropEntityId_RightHalf_ReturnsShopSideEntity()
    {
        var (window, _, _) = Build();
        var rightPoint = new Point((int)(window.ContentAbsolutePosition.X + window.ContentSize.X) - 1, (int)window.ContentAbsolutePosition.Y + 1);

        Assert.AreEqual(TradeShopEntityId, window.ResolveItemDropEntityId(rightPoint));
        Assert.AreEqual(TradeShopEntityId, window.ResolveCurrencyDropEntityId(rightPoint));
    }

    /// <summary>
    /// End-to-end proof that UiInputController.FindDropTargetEntityId's own IWholeWindowDropTarget
    /// fallback actually fires -- dropping an item drag on TradeWindow's own header area (y &lt;
    /// HeaderHeight, above both columns' grids -- no grid/currency child sits there at all, so the
    /// narrower IInventoryDropTarget check finds nothing) must still resolve to the player-side
    /// trade column, not silently fail, since the drop point is left of this window's own midpoint.
    /// </summary>
    [TestMethod]
    public void Drag_FromPlayerInventoryOntoTradeWindowHeaderArea_StillResolvesToPlayerSideColumn()
    {
        var (tradeWindow, componentManager, mapViewState) = Build();

        var fontService = TestFonts.Shared;
        var labelRenderer = new LabelRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, labelRenderer);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        // ShopItemStackCell too -- mapViewState.OpenShopEntityId is already set (from Build()'s own
        // harness), which puts every InventoryGridContent into shop mode, this player grid included.
        windowService.RegisterFactory<ShopItemStackCell>(() => new ShopItemStackCell(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(fontService, windowService, labelRenderer));

        var world = new Game.World.World(new Game.World.Map(new Vector3Int(10, 10, 1))) { PlayerEntityId = PlayerEntityId };
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(windowService, new UiLayerStack());
        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(PotionItemId, "Test Potion", null, "p", Color.White, Tags: [Tag.Potion], Effects: [], GoldValue: 10));
        InventoryActions.AddItem(componentManager, PlayerEntityId, PotionItemId, quantity: 1);

        var hoverPopup = windowService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = new Vector2(220, 220), DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        hoverPopup.Initialize();

        // Positioned well away from the trade window (which sits at/near the origin, RelativePosition
        // Vector2.Zero, no parent) so the two never overlap on screen.
        var playerGridWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(500, 500), Size = new Vector2(200, 200), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
        });
        playerGridWindow.SetContent(new InventoryGridContent(world, componentManager, itemCatalog, windowService, fontService, labelRenderer, spriteSheetService, spriteRenderer, contextMenuController, PlayerEntityId, filterTag: null, hoverPopup, static () => null, mapViewState, static (_, _) => { }, static (_, _) => { }));
        playerGridWindow.Initialize();

        var layers = new UiLayerStack();
        layers.Add(UiLayer.DynamicHud, playerGridWindow);
        layers.Add(UiLayer.DynamicHud, tradeWindow);
        var controller = new UiInputController(layers, new Vector2(2000, 2000), componentManager: componentManager, playerQuery: world, itemCatalog: itemCatalog, mapViewState: mapViewState);

        var cell = playerGridWindow.ChildElements.OfType<InventoryItemStackCell>().Single();
        var pressPoint = cell.ContentRectangle.Center;
        controller.Update(new KeyboardState(), MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(new KeyboardState(), MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        var dropPoint = new Point((int)tradeWindow.ContentAbsolutePosition.X + 5, (int)tradeWindow.ContentAbsolutePosition.Y + 5);
        controller.Update(new KeyboardState(), MouseAt(dropPoint.X, dropPoint.Y, ButtonState.Released));

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsFalse(InventoryQueries.TryGetStack(stacks, PlayerEntityId, PotionItemId, out _), "The item must have left the player's own inventory.");
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, TradePlayerEntityId, PotionItemId, out _), "-- landing in the player-side trade column via the whole-window fallback (the drop point was in TradeWindow's own header area, left of its own midpoint).");
    }

    /// <summary>
    /// PLAN-trade-window.md's own last open test-coverage item: confirms an item stack dropped
    /// directly on the *currency footer* of the player-side column (not that column's own item
    /// grid) still stages into the player-side trade entity, never miscategorized as a currency
    /// transfer. Targets the player-side column specifically -- a direct drag from the player's
    /// real inventory onto the *shop*-side column is a separate, deliberately disallowed
    /// combination (see UiInputControllerTests.Drag_PlayerInventoryToTradeShopColumn_IsNotAllowed),
    /// unrelated to what this test is checking. This isn't actually special-cased routing logic --
    /// both the grid and currency-footer child windows in one TradeWindow column are built against
    /// the exact same trade-offer entityId (see TradeWindow.BuildColumn), and
    /// FindDropTargetEntityId's IInventoryDropTarget branch (checked before the
    /// IWholeWindowDropTarget/isCurrencyDrag fallback ever applies) resolves through whichever
    /// window's own Tag the drop actually lands on, regardless of payload type -- so the two
    /// windows sharing one entityId is what makes this safe by construction, confirmed here
    /// end-to-end.
    /// </summary>
    [TestMethod]
    public void Drag_FromPlayerInventoryOntoTradeWindowPlayerColumnCurrencyRow_StillResolvesToPlayerSideItemGrid()
    {
        var (tradeWindow, componentManager, mapViewState) = Build();

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
        itemCatalog.Register(new ItemDefinition(PotionItemId, "Test Potion", null, "p", Color.White, Tags: [Tag.Potion], Effects: [], GoldValue: 10));
        InventoryActions.AddItem(componentManager, PlayerEntityId, PotionItemId, quantity: 1);

        var hoverPopup = windowService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = new Vector2(220, 220), DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        hoverPopup.Initialize();

        var playerGridWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(500, 500), Size = new Vector2(200, 200), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
        });
        playerGridWindow.SetContent(new InventoryGridContent(world, componentManager, itemCatalog, windowService, fontService, labelRenderer, spriteSheetService, spriteRenderer, contextMenuController, PlayerEntityId, filterTag: null, hoverPopup, static () => null, mapViewState, static (_, _) => { }, static (_, _) => { }));
        playerGridWindow.Initialize();

        var layers = new UiLayerStack();
        layers.Add(UiLayer.DynamicHud, playerGridWindow);
        layers.Add(UiLayer.DynamicHud, tradeWindow);
        var controller = new UiInputController(layers, new Vector2(2000, 2000), componentManager: componentManager, playerQuery: world, itemCatalog: itemCatalog, mapViewState: mapViewState);

        var cell = playerGridWindow.ChildElements.OfType<InventoryItemStackCell>().Single();
        var pressPoint = cell.ContentRectangle.Center;
        controller.Update(new KeyboardState(), MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(new KeyboardState(), MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        var playerFooterWindow = tradeWindow.ChildElements.OfType<Window>().Single(w => w.Content is CurrencyRowContent row && row.EntityId == TradePlayerEntityId);
        var dropPoint = playerFooterWindow.ContentRectangle.Center;
        controller.Update(new KeyboardState(), MouseAt(dropPoint.X, dropPoint.Y, ButtonState.Released));

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsFalse(InventoryQueries.TryGetStack(stacks, PlayerEntityId, PotionItemId, out _), "The item must have left the player's own inventory.");
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, TradePlayerEntityId, PotionItemId, out _), "-- landing in the player-side trade column's item stacks, not treated as a currency drop and not misrouted to the shop-side column.");
        Assert.AreEqual(0, ReadGold(componentManager, TradePlayerEntityId), "No Gold must have moved -- this was an item drag, not a currency drag, even though it landed on a currency-hosting window.");
    }
}
