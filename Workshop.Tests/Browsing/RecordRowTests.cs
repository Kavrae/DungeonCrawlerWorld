using Builders.Browsing;
using Builders.Editing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Fonts;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;
using UIEngine.UI.Chrome;

namespace Workshop.Tests.Browsing;

/// <summary>A browser row over a real layer stack and UiInputController: the name cut to what its marker symbols leave, each symbol its own tooltip, and a click on a symbol reaching the row.</summary>
[TestClass]
[DoNotParallelize]
public sealed class RecordRowTests
{
    private const float RowWidth = 330f;

    private static readonly Vector2 ScreenSize = new(1600, 900);
    private static readonly FontService SharedFonts = TestFonts.Shared;

    private ElementPoolService _pool = null!;
    private UiLayerStack _layers = null!;
    private UiInputController _input = null!;
    private Tooltip _tooltip = null!;
    private Window _host = null!;

    [TestInitialize]
    public void Initialize()
    {
        var render = new UiRenderService();
        _pool = new ElementPoolService();
        _layers = new UiLayerStack();
        var services = new ElementServices(SharedFonts, _pool, render);
        var spriteSheets = new SpriteSheetService(null, "Spritesheets", SpriteManifestResolver.Load(Path.Combine(AppContext.BaseDirectory, "SpriteManifest.json")));
        _pool.RegisterFactory(() => new Window(services));
        _pool.RegisterFactory(() => new TextWindow(services));
        _pool.RegisterFactory(() => new Tooltip(services));
        _pool.RegisterFactory(() => new IconElement(services, spriteSheets));
        _pool.RegisterFactory(() => new Label(services));
        var tooltips = new TooltipController { ScreenBoundsOverrideForTests = new Rectangle(0, 0, (int)ScreenSize.X, (int)ScreenSize.Y) };
        tooltips.Initialize(_pool, _layers);
        _tooltip = _layers[UiLayer.Tooltip].OfType<Tooltip>().Single();
        _input = new UiInputController(_layers, ScreenSize, new PointerState(), [], [], tooltipController: tooltips);

        _host = _pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(20, 20), Size = new Vector2(RowWidth + 20, 400), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false },
        });
        _host.Initialize();
        _layers.Add(UiLayer.Base, _host);
    }

    private Window AddRow(string name, ContentRecordState state, string? replacementName = null)
    {
        var appearance = new RecordRowAppearance(name, null, "!", Color.White)
        {
            Markers = ContentRecordMarkers.Of(state, replacementName),
            IsDimmed = ContentRecordMarkers.IsDimmed(state),
        };
        var row = RecordRow.Create(_pool, _host, RowWidth, appearance, Color.Black);
        _host.AddChild(row);
        return row;
    }

    private static Label NameOf(Window row) => row.ChildElements.OfType<Label>().Single();

    private static List<IconElement> SymbolsOf(Window row) => [.. row.ChildElements.OfType<IconElement>().Skip(1)];

    private void Hover(Point position, int frames)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            _input.Update(new KeyboardState(), new MouseState(position.X, position.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
        }
    }

    private void Click(Point position, bool right = false)
    {
        Hover(position, 1);
        var left = right ? ButtonState.Released : ButtonState.Pressed;
        var rightButton = right ? ButtonState.Pressed : ButtonState.Released;
        _input.Update(new KeyboardState(), new MouseState(position.X, position.Y, 0, left, ButtonState.Released, rightButton, ButtonState.Released, ButtonState.Released));
        Hover(position, 1);
    }

    [TestMethod]
    public void ALongName_IsCutToWhatTheSymbolsLeave_AndEverySymbolIsInsideTheRow()
    {
        var row = AddRow("A Remarkably Long Item Name That Will Never Fit Beside Its Markers", ContentRecordState.Unsaved | ContentRecordState.Deleted | ContentRecordState.Disabled | ContentRecordState.Replaced | ContentRecordState.HasErrors, "Scroll of Healing");
        var name = NameOf(row);
        var symbols = SymbolsOf(row);

        Assert.HasCount(4, symbols);
        StringAssert.EndsWith(name.ShownText, "...");
        Assert.IsLessThanOrEqualTo(symbols[0].Rectangle.Left, name.Rectangle.Right);
        foreach (var symbol in symbols)
        {
            Assert.IsTrue(row.Rectangle.Contains(symbol.Rectangle), symbol.TooltipText);
        }

        for (var index = 1; index < symbols.Count; index++)
        {
            Assert.IsGreaterThan(symbols[index - 1].Rectangle.Right - 1, symbols[index].Rectangle.Left);
        }
    }

    [TestMethod]
    public void AShortName_IsWhole_AndTheRowHasNoTooltip()
    {
        var row = AddRow("Potion", ContentRecordState.Draft);

        Assert.AreEqual("Potion", NameOf(row).ShownText);
        Assert.IsNull(row.TooltipText);
        Assert.IsNull(NameOf(row).TooltipText);
    }

    [TestMethod]
    public void HoveringEachSymbol_ShowsItsOwnTooltip_AndHoveringACutName_ShowsTheFullName()
    {
        const string longName = "A Remarkably Long Item Name That Will Never Fit Beside Its Markers";
        var row = AddRow(longName, ContentRecordState.Disabled | ContentRecordState.Replaced | ContentRecordState.HasErrors, "Scroll of Healing");

        var shown = new List<string>();
        foreach (var symbol in SymbolsOf(row))
        {
            Hover(symbol.Rectangle.Center, PopupChrome.HoverTooltipDelayFrames);
            Assert.IsTrue(_tooltip.IsVisible);
            shown.Add(_tooltip.OriginalText);
        }

        CollectionAssert.AreEqual(new[] { "Replaced by Scroll of Healing", "Has errors" }, shown);

        Hover(NameOf(row).Rectangle.Center, PopupChrome.HoverTooltipDelayFrames);
        Assert.AreEqual(longName, _tooltip.OriginalText);
    }

    [TestMethod]
    public void ClickingOrRightClickingASymbol_ReachesTheRow()
    {
        var row = AddRow("Potion", ContentRecordState.Draft | ContentRecordState.HasErrors);
        var clicks = 0;
        var rightClicks = 0;
        row.Clicked += _ => clicks++;
        row.OnRightClicked = _ => rightClicks++;
        var symbol = SymbolsOf(row)[1];

        Click(symbol.Rectangle.Center);
        Click(symbol.Rectangle.Center, right: true);

        Assert.AreEqual(1, clicks);
        Assert.AreEqual(1, rightClicks);
    }
}
