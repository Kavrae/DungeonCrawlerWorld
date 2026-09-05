using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
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
/// CurrencyRowContent had no dedicated test coverage before it became an IElementContent (see
/// TODO.md's "Element footer" item) -- this covers its own Initialize/Reposition contract
/// directly, independent of whichever Window happens to host it (InventoryManagementWindow,
/// SecondaryInventoryWindow).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CurrencyRowContentTests
{
    private const int EntityId = 1;

    private static (CurrencyRowContent Content, Window HostWindow) Build(float hostWidth = 100f)
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        var fontService = TestFonts.Shared;
        var labelRenderer = new LabelRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, labelRenderer);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        windowService.RegisterFactory<CurrencyElement>(() => new CurrencyElement(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));

        var world = new Game.World.World(new Game.World.Map(new Vector3Int(10, 10, 1)));
        var contextMenuController = new ContextMenuController(windowService);
        contextMenuController.Initialize(new UiLayerStack());

        var content = new CurrencyRowContent(EntityId, componentManager, world, contextMenuController, windowService, fontService, labelRenderer, spriteSheetService, spriteRenderer, static () => null);

        var hostWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(hostWidth, CurrencyRowContent.Height), DisplayMode = ElementDisplayMode.Fixed },
        });
        hostWindow.ContentPadding = Vector2.Zero;
        hostWindow.SetContent(content);
        hostWindow.Initialize();

        return (content, hostWindow);
    }

    [TestMethod]
    public void Initialize_SetsHostWindowTagToItself()
    {
        var (content, hostWindow) = Build();

        Assert.AreSame(content, hostWindow.Tag, "FindDropTargetEntityId resolves a drop via Window.Tag -- see IInventoryDropTarget's own doc comment.");
    }

    [TestMethod]
    public void Initialize_SplitsHostContentWidthInHalf_GoldLeftCreditsRight()
    {
        var (_, hostWindow) = Build(hostWidth: 100f);

        var goldElement = hostWindow.ChildElements.OfType<CurrencyElement>().Single(element => element.Type == CurrencyType.Gold);
        var creditsElement = hostWindow.ChildElements.OfType<CurrencyElement>().Single(element => element.Type == CurrencyType.Credits);

        Assert.AreEqual(0f, goldElement.RelativePosition.X);
        Assert.AreEqual(hostWindow.ContentSize.X / 2f, goldElement.CurrentSize.X);
        Assert.AreEqual(hostWindow.ContentSize.X / 2f, creditsElement.RelativePosition.X);
        Assert.AreEqual(hostWindow.ContentSize.X - creditsElement.RelativePosition.X, creditsElement.CurrentSize.X);

        Assert.IsGreaterThan(0, goldElement.CurrentSize.Y, "Regression check: currency elements must have real, nonzero height (see Element.ComputeChildAvailableSize).");
    }

    [TestMethod]
    public void HostWindowResized_ReSplitsCurrencyElementsToNewWidth()
    {
        var (_, hostWindow) = Build(hostWidth: 100f);

        hostWindow.SetSize(new Vector2(200f, CurrencyRowContent.Height));

        var goldElement = hostWindow.ChildElements.OfType<CurrencyElement>().Single(element => element.Type == CurrencyType.Gold);
        var creditsElement = hostWindow.ChildElements.OfType<CurrencyElement>().Single(element => element.Type == CurrencyType.Credits);

        Assert.AreEqual(hostWindow.ContentSize.X / 2f, goldElement.CurrentSize.X);
        Assert.AreEqual(hostWindow.ContentSize.X / 2f, creditsElement.RelativePosition.X);
    }

    private const int PlayerEntityId = 10;
    private const int ShopEntityId = 11;

    /// <summary>
    /// Builds a row for rowEntityId, with world.PlayerEntityId fixed to PlayerEntityId and
    /// getSecondaryTargetEntityId fixed to secondaryTargetEntityId -- enough to drive
    /// BuildCurrencyContextMenu's own Give/Take decision (see CurrencyRowContent's own doc
    /// comment) without needing a real InventoryManagementWindow/ShopWindow around it. Unlike
    /// Build() above, registers CurrencyComponent and ShopComponent so TransferOne/TransferAll's
    /// real balance reads/shop checks have something to act on.
    /// </summary>
    private static (CurrencyElement GoldElement, ComponentManager ComponentManager, ContextMenuController ContextMenuController) BuildForGiveTake(int rowEntityId, int secondaryTargetEntityId, EventBus? eventBus = null)
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<CurrencyComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ShopComponent>(static (ref existing, incoming) => existing = incoming);
        var fontService = TestFonts.Shared;
        var labelRenderer = new LabelRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, labelRenderer);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        windowService.RegisterFactory<CurrencyElement>(() => new CurrencyElement(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));

        var world = new Game.World.World(new Game.World.Map(new Vector3Int(10, 10, 1))) { PlayerEntityId = PlayerEntityId };
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(windowService, new UiLayerStack());

        var content = new CurrencyRowContent(rowEntityId, componentManager, world, contextMenuController, windowService, fontService, labelRenderer, spriteSheetService, spriteRenderer, () => secondaryTargetEntityId, eventBus);

        var hostWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(100f, CurrencyRowContent.Height), DisplayMode = ElementDisplayMode.Fixed },
        });
        hostWindow.ContentPadding = Vector2.Zero;
        hostWindow.SetContent(content);
        hostWindow.Initialize();

        var goldElement = hostWindow.ChildElements.OfType<CurrencyElement>().Single(element => element.Type == CurrencyType.Gold);
        return (goldElement, componentManager, contextMenuController);
    }

    [TestMethod]
    public void RightClickPlayerRow_SecondaryTargetIsShop_OffersOnlyGiveAllNotGiveOrTake()
    {
        var (goldElement, componentManager, contextMenuController) = BuildForGiveTake(PlayerEntityId, ShopEntityId);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: null, buyMultiplier: 1.2f, sellMultiplier: 0.8f));

        goldElement.OnRightClicked!.Invoke(Point.Zero);

        Assert.IsTrue(contextMenuController.IsOpen);
        var labels = contextMenuController.Menu.ChildElements.OfType<Button>().Select(button => button.LeftText).ToList();
        CollectionAssert.DoesNotContain(labels, "Give", "A shop only ever offers \"Give All\" -- see CurrencyRowContent.BuildCurrencyContextMenu's own ShopComponent check.");
        CollectionAssert.Contains(labels, "Give All");
        CollectionAssert.DoesNotContain(labels, "Take");
        CollectionAssert.DoesNotContain(labels, "Take All");
    }

    [TestMethod]
    public void RightClickShopRow_SecondaryTargetIsItself_OffersNoOptionsAtAll()
    {
        var (shopGoldElement, componentManager, contextMenuController) = BuildForGiveTake(ShopEntityId, ShopEntityId);
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: null, buyMultiplier: 1.2f, sellMultiplier: 0.8f));

        shopGoldElement.OnRightClicked!.Invoke(Point.Zero);

        Assert.IsFalse(contextMenuController.IsOpen, "A shop can never Take/Take All its own currency back from the player -- see CurrencyRowContent.BuildCurrencyContextMenu's own ShopComponent check.");
    }

    [TestMethod]
    public void GiveAllGoldToShop_PublishesGoldGivenToShopEventWithTheAmountGiven()
    {
        var eventBus = new EventBus();
        var (goldElement, componentManager, contextMenuController) = BuildForGiveTake(PlayerEntityId, ShopEntityId, eventBus);
        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 42, credits: 0));
        componentManager.Merge(ShopEntityId, new ShopComponent(allowedTags: null, buyMultiplier: 1.2f, sellMultiplier: 0.8f));

        GoldGivenToShopEvent? published = null;
        eventBus.Subscribe<GoldGivenToShopEvent>(e => published = e);

        goldElement.OnRightClicked!.Invoke(Point.Zero);
        var giveAllButton = contextMenuController.Menu.ChildElements.OfType<Button>().Single(button => button.LeftText == "Give All");
        contextMenuController.Menu.HandleClick(giveAllButton.Rectangle.Center);

        Assert.IsNotNull(published);
        Assert.AreEqual(PlayerEntityId, published!.Value.PlayerEntityId);
        Assert.AreEqual(ShopEntityId, published.Value.ShopEntityId);
        Assert.AreEqual(42, published.Value.Amount);
        Assert.AreEqual(0, componentManager.GetPackedPool<CurrencyComponent>().GetReadonly(PlayerEntityId).Gold, "The whole Gold balance moved -- a shop only ever offers Give All (see BuildCurrencyContextMenu's own ShopComponent check), which still moves the entire amount per currency type, same as CurrencyActions.TryTransfer's own doc comment.");
    }

    [TestMethod]
    public void GiveGoldToNonShopTarget_DoesNotPublishGoldGivenToShopEvent()
    {
        const int corpseEntityId = 12;
        var eventBus = new EventBus();
        var (goldElement, componentManager, contextMenuController) = BuildForGiveTake(PlayerEntityId, corpseEntityId, eventBus);
        componentManager.Merge(PlayerEntityId, new CurrencyComponent(gold: 42, credits: 0));

        var publishedCount = 0;
        eventBus.Subscribe<GoldGivenToShopEvent>(_ => publishedCount++);

        goldElement.OnRightClicked!.Invoke(Point.Zero);
        var giveButton = contextMenuController.Menu.ChildElements.OfType<Button>().Single(button => button.LeftText == "Give");
        contextMenuController.Menu.HandleClick(giveButton.Rectangle.Center);

        Assert.AreEqual(0, publishedCount);
        Assert.AreEqual(0, componentManager.GetPackedPool<CurrencyComponent>().GetReadonly(PlayerEntityId).Gold, "Give itself must still have happened -- only the shop-specific event is what's suppressed for a non-shop target.");
    }
}
