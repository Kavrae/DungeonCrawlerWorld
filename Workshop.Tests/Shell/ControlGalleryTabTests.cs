using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Bootstrap;
using UIEngine.Fonts;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Workshop.Tests.Shell;

[TestClass]
[DoNotParallelize]
public sealed class ControlGalleryTabTests
{
    private static readonly Vector2 ScreenSize = new(1600, 900);

    private static readonly FontService SharedFonts = TestFonts.Shared;

    private sealed class RecordingStatus : IWorkshopStatus
    {
        public List<string> Messages { get; } = [];

        public void Show(string message) => Messages.Add(message);
    }

    private sealed class Harness
    {
        private MouseState _mouse;

        public Harness()
        {
            var render = new UiRenderService();
            var pool = new ElementPoolService();
            var sprites = new SpriteSheetService(null, "Spritesheets", SpriteManifestResolver.Load(Path.Combine(AppContext.BaseDirectory, "SpriteManifest.json")));
            var uiEngine = new UiEngineContext(SharedFonts, null!, null!, render, sprites, pool);
            Layers = new UiLayerStack();
            var dropdowns = new DropdownController(pool, render) { ScreenBoundsOverrideForTests = new Rectangle(0, 0, (int)ScreenSize.X, (int)ScreenSize.Y) };
            var contextMenus = new ContextMenuController(dropdowns);
            uiEngine.RegisterFrameworkElementFactories(contextMenus);
            dropdowns.Initialize(Layers);
            pool.RegisterFactory(() => new WorkshopBottomPanel(uiEngine.ElementServices));
            Input = new UiInputController(Layers, ScreenSize, new PointerState(), [], [], dropdowns) { StartTextInput = static () => { }, StopTextInput = static () => { } };

            var navigator = new TabNavigator([Tab], _ => { }, Status);
            var bottomPanel = pool.CreateElement<WorkshopBottomPanel>(null, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
                Layout = new ElementLayoutOptions { Size = new Vector2(800, 200), DisplayMode = ElementDisplayMode.Fixed },
            });
            bottomPanel.Configure(navigator, contextMenus);
            bottomPanel.Initialize();

            Tab.Open(new WorkshopTabContext(uiEngine, Layers, contextMenus, Status, Input, navigator, bottomPanel), new Rectangle(0, 30, (int)ScreenSize.X, (int)ScreenSize.Y - 30));
            Frame();
        }

        public ControlGalleryTab Tab { get; } = new();

        public RecordingStatus Status { get; } = new();

        public UiLayerStack Layers { get; }

        public UiInputController Input { get; }

        public Window Gallery => Layers[UiLayer.Base].OfType<Window>().Single(window => window.TitleText == ControlGalleryTab.TabTitle);

        public void Frame()
        {
            Input.Update(new KeyboardState(), _mouse);
            foreach (var layer in UiLayerStack.LayersAscending())
            {
                foreach (var element in Layers[layer].ToArray())
                {
                    element.Update(new GameTime());
                }
            }
        }

        public void Click(Point position)
        {
            _mouse = new MouseState(position.X, position.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
            Frame();
            _mouse = new MouseState(position.X, position.Y, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
            Frame();
            _mouse = new MouseState(position.X, position.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
            Frame();
        }

        /// <summary>The control in the row captioned caption.</summary>
        public Element ControlCaptioned(string caption)
        {
            var captionLabel = Gallery.ChildElements.OfType<Label>().Single(label => label.Text == caption);
            return Gallery.ChildElements.First(element => element is not Label && element.Rectangle.Top == captionLabel.Rectangle.Top);
        }
    }

    [TestMethod]
    public void Open_HeadsEveryGroup()
    {
        var harness = new Harness();

        var headingCount = harness.Gallery.ChildElements.OfType<TextDivider>().Count();

        Assert.AreEqual(7, headingCount, "six group headings and the Text divider row");
    }

    [TestMethod]
    [DataRow("Button", "Button: pressed", DisplayName = "Button_Click_ShowsOnTheStatusLine")]
    [DataRow("Toggle", "Toggle: off", DisplayName = "Toggle_Click_ShowsItsNewStateOnTheStatusLine")]
    public void AControl_Click_ShowsWhatHappenedOnTheStatusLine(string caption, string expectedMessage)
    {
        var harness = new Harness();

        harness.Click(harness.ControlCaptioned(caption).Rectangle.Center);

        CollectionAssert.Contains(harness.Status.Messages, expectedMessage);
    }

    [TestMethod]
    public void DisabledToggle_Click_ShowsNothing()
    {
        var harness = new Harness();

        harness.Click(harness.ControlCaptioned("Disabled toggle").Rectangle.Center);

        Assert.AreEqual(0, harness.Status.Messages.Count);
    }
}
