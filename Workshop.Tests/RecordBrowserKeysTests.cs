using Builders.Browsing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Bootstrap;
using UIEngine.Fonts;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Workshop.Tests;

[TestClass]
[DoNotParallelize]
public sealed class RecordBrowserKeysTests
{
    private static readonly Vector2 ScreenSize = new(1600, 900);

    private static readonly FontService SharedFonts = TestFonts.Shared;

    private sealed class TestRecord(string name)
    {
        public Guid Id { get; } = Guid.NewGuid();

        public string Name { get; } = name;

        public bool CanDelete { get; init; } = true;
    }

    /// <summary>A browser over two headed sections, with an inspector holding three tab stops, a Delete command refused for a record that can't be deleted, and New.</summary>
    private sealed class TestTab : RecordBrowserTab<TestRecord, Window>
    {
        public List<TestRecord> Records { get; } =
        [
            new("Alpha"), new("Beta"), new("Gamma") { CanDelete = false },
            new("Delta"), new("Epsilon"),
        ];

        public List<TestRecord> Deleted { get; } = [];

        public List<Element> InspectorTabStops { get; } = [];

        public override string Title => "Things";

        protected override string BrowserTitle => $"Things ({Records.Count})";

        protected override string SearchGhostText => "Search";

        protected override string PreviewTitle => "Preview";

        protected override bool HasInspector => true;

        protected override string? NewRecordLabel => "New";

        protected override IEnumerable<TestRecord> AllRecords => Records;

        protected override Guid IdOf(TestRecord record) => record.Id;

        public TestRecord? Selected => SelectedRecord;

        protected override IReadOnlyList<RecordBrowserSection<TestRecord>> BuildSections(string search) =>
        [
            new("First", [.. Records.Take(3)]),
            new("Second", [.. Records.Skip(3)]),
        ];

        protected override RecordRowAppearance AppearanceOf(TestRecord record) => new(record.Name, null, "?", Color.White);

        protected override void ShowInPreview(Window preview, TestRecord record, float contentWidth)
        {
        }

        protected override TestRecord? CreateNewRecord()
        {
            var created = new TestRecord("New");
            Records.Add(created);
            return created;
        }

        protected override IReadOnlyList<RecordCommand> CommandsFor(TestRecord record) =>
        [
            new RecordCommand("Duplicate", "Copy it.", true, () => { }),
            new RecordCommand("Delete", record.CanDelete ? "Delete it." : "Can't delete: something names it", record.CanDelete, () =>
            {
                Deleted.Add(record);
                Records.Remove(record);
                RefreshRows();
            }) { Key = Keys.Delete },
        ];

        protected override void ShowInInspector(Window inspector, TestRecord record)
        {
            var pool = Context.UiEngine.ElementPoolService;
            pool.CloseAllChildren(inspector);
            InspectorTabStops.Clear();
            InspectorTabStops.Add(Add<TextBox>(inspector, pool));
            InspectorTabStops.Add(Add<Toggle>(inspector, pool));
            InspectorTabStops.Add(Add<TextBox>(inspector, pool));
        }

        private static T Add<T>(Window inspector, ElementPoolService pool)
            where T : Element
        {
            var control = pool.CreateElement<T>(inspector, new ElementOptions
            {
                Layout = new ElementLayoutOptions { Size = new Vector2(200, 24), DisplayMode = ElementDisplayMode.Fixed },
            });
            inspector.AddChild(control);
            return control;
        }
    }

    private sealed class RecordingStatus : IWorkshopStatus
    {
        public List<string> Messages { get; } = [];

        public void Show(string message) => Messages.Add(message);
    }

    private sealed class Harness
    {
        private KeyboardState _keyboard;
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

        public TestTab Tab { get; } = new();

        public RecordingStatus Status { get; } = new();

        public UiLayerStack Layers { get; }

        public UiInputController Input { get; }

        public void Frame()
        {
            Input.Update(_keyboard, _mouse);
            foreach (var layer in UiLayerStack.LayersAscending())
            {
                foreach (var element in Layers[layer].ToArray())
                {
                    element.Update(new GameTime());
                }
            }

            Tab.Update(new GameTime());
        }

        public void Press(Keys key)
        {
            _keyboard = new KeyboardState(key);
            Frame();
            _keyboard = new KeyboardState();
            Frame();
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

        public Window Browser => Layers[UiLayer.Base].OfType<Window>().First(window => window.TitleText.StartsWith("Things", StringComparison.Ordinal));

        public Window Inspector => Layers[UiLayer.Base].OfType<Window>().First(window => window.TitleText == "Inspector");

        /// <summary>The browser's row showing name.</summary>
        public Element RowOf(string name) => Browser.ChildElements.Last().ChildElements.First(row => row.ChildElements.OfType<Label>().Any(label => label.Text == name));

        public TextBox SearchBox => Browser.ChildElements.OfType<TextBox>().Single();

        public void ClickRow(string name) => Click(RowOf(name).Rectangle.Center);
    }

    [TestMethod]
    public void UpAndDown_StepThroughTheListedRecords_SkippingHeadings_AndStopAtTheEnds()
    {
        var harness = new Harness();
        harness.ClickRow("Beta");

        harness.Press(Keys.Down);
        Assert.AreEqual("Gamma", harness.Tab.Selected?.Name);
        harness.Press(Keys.Down);
        Assert.AreEqual("Delta", harness.Tab.Selected?.Name, "The heading between them is skipped.");
        harness.Press(Keys.Up);
        harness.Press(Keys.Up);
        harness.Press(Keys.Up);
        harness.Press(Keys.Up);
        Assert.AreEqual("Alpha", harness.Tab.Selected?.Name);
        harness.Press(Keys.PageDown);
        Assert.AreNotEqual("Alpha", harness.Tab.Selected?.Name);
        harness.Press(Keys.End);
        Assert.AreEqual("Epsilon", harness.Tab.Selected?.Name);
    }

    [TestMethod]
    public void Delete_RunsTheDeleteCommand_OrSaysWhyItCant()
    {
        var harness = new Harness();
        harness.ClickRow("Gamma");

        harness.Press(Keys.Delete);
        Assert.IsEmpty(harness.Tab.Deleted);
        CollectionAssert.Contains(harness.Status.Messages, "Can't delete: something names it");

        harness.Press(Keys.Down);
        harness.Press(Keys.Delete);
        Assert.AreEqual("Delta", harness.Tab.Deleted.Single().Name);
    }

    [TestMethod]
    public void Insert_MakesANewRecord_AsNewDoes()
    {
        var harness = new Harness();
        harness.ClickRow("Alpha");

        harness.Press(Keys.Insert);

        Assert.AreEqual("New", harness.Tab.Selected?.Name);
        Assert.HasCount(6, harness.Tab.Records);
    }

    [TestMethod]
    public void TheSearchBox_KeepsItsKeys()
    {
        var harness = new Harness();
        harness.ClickRow("Alpha");
        harness.Input.FocusElement(harness.SearchBox);

        harness.Press(Keys.Down);
        harness.Press(Keys.Delete);
        harness.Press(Keys.Insert);

        Assert.AreEqual("Alpha", harness.Tab.Selected?.Name);
        Assert.IsEmpty(harness.Tab.Deleted);
        Assert.HasCount(5, harness.Tab.Records);
    }

    [TestMethod]
    public void HomeAndEnd_InTheInspector_FocusItsFirstAndLastTabStops()
    {
        var harness = new Harness();
        harness.ClickRow("Alpha");
        var stops = harness.Tab.InspectorTabStops;
        harness.Input.FocusElement(stops[1]);

        harness.Press(Keys.End);
        Assert.IsTrue(stops[2].IsFocused);

        harness.Input.FocusElement(stops[1]);
        harness.Press(Keys.Home);
        Assert.IsTrue(stops[0].IsFocused);
        Assert.AreEqual("Alpha", harness.Tab.Selected?.Name, "The inspector's keys never reach the list.");
    }
}
