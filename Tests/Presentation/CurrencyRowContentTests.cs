using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Currency;
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
}
