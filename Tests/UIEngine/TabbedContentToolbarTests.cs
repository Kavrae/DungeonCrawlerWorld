using Engine.Utilities;
using Microsoft.Xna.Framework;
using UIEngine.UI;
using UIEngine.UI.Content;

namespace Tests.UIEngine;

/// <summary>A TabbedContent's shared toolbar keeps its search text, its search box's focus and its filter across tab switches, its active tab is updated once a frame, and focus never stays on an element returned to its pool -- through a real UiInputController.</summary>
[TestClass]
[DoNotParallelize]
public sealed class TabbedContentToolbarTests
{
    /// <summary>A tab that reads the toolbar's search as it opens and follows it while shown.</summary>
    private sealed class FilteredTab(GridControlToolbar toolbar) : IElementContent
    {
        public string? AppliedFilter { get; private set; }

        public void Initialize(Window hostWindow)
        {
            AppliedFilter = toolbar.AppliedSearchText;
            toolbar.SearchFilterChanged += OnSearchFilterChanged;
        }

        public int Updates { get; private set; }

        public void Update(GameTime gameTime)
        {
            Updates++;
        }

        public void DrawContent(GameTime gameTime)
        {
        }

        public void Deactivate()
        {
            toolbar.SearchFilterChanged -= OnSearchFilterChanged;
            AppliedFilter = null;
        }

        private void OnSearchFilterChanged(string text) => AppliedFilter = text;
    }

    private static IEnumerable<Element> SelfAndDescendants(Element element) =>
        element.ChildElements.SelectMany(SelfAndDescendants).Prepend(element);

    private static (ControlTestHarness Harness, Window Window, FilteredTab First, FilteredTab Second) Build()
    {
        var harness = new ControlTestHarness();
        harness.Pool.RegisterFactory(() => new GridControl(TestElementServices.For(harness.Pool, TestFonts.Shared)));
        var toolbar = new GridControlToolbar(harness.Pool, ["A-Z"], [], "Search");
        var first = new FilteredTab(toolbar);
        var second = new FilteredTab(toolbar);
        var tabs = new TabbedContent([new("First", first), new("Second", second)], harness.Pool, TestFonts.Shared, toolbar: toolbar, toolbarHeight: GridControl.RowHeight);
        var window = harness.AddRoot<Window>(new Vector2(100, 100), new Vector2(500, 300), window => window.SetContent(tabs));
        return (harness, window, first, second);
    }

    private static TextBox ToolbarSearchBox(Window window) =>
        SelfAndDescendants(window).OfType<GridControl>().Single().ChildElements.OfType<TextBox>().Single();

    private static Button TabTile(Window window, string label) =>
        SelfAndDescendants(window).OfType<Button>().Single(button => button.Text == label);

    private static void WaitOutTheSearchDebounce(ControlTestHarness harness)
    {
        for (var frame = 0; frame < GameTiming.FramesForSeconds(0.5f); frame++)
        {
            harness.Frame();
        }
    }

    [TestMethod]
    public void SwitchingTabs_KeepsTheSearchText_ItsFocus_AndAppliesItToTheNewTab()
    {
        var (harness, window, first, second) = Build();
        var searchBox = ToolbarSearchBox(window);

        harness.Click(searchBox);
        harness.Type("fire");
        WaitOutTheSearchDebounce(harness);
        Assert.AreEqual("fire", first.AppliedFilter);

        harness.Click(TabTile(window, "Second"));

        Assert.AreSame(searchBox, ToolbarSearchBox(window), "The toolbar is never rebuilt by a tab switch.");
        Assert.AreEqual("fire", searchBox.OriginalText);
        Assert.AreEqual("fire", second.AppliedFilter);
        Assert.IsTrue(searchBox.IsFocused);
        Assert.IsTrue(harness.Input.IsTextBoxFocused);
    }

    [TestMethod]
    public void AFocusedTextBoxReturnedToItsPool_LosesFocus_AndTheInstanceReusedCanBeFocusedByAClick()
    {
        var harness = new ControlTestHarness();
        var window = harness.AddRoot<Window>(new Vector2(100, 100), new Vector2(300, 200));
        var first = AddTextBox(harness, window);
        harness.Click(first);
        Assert.IsTrue(first.IsFocused);

        harness.Pool.CloseAllChildren(window);
        var reused = AddTextBox(harness, window);
        harness.Frame();

        Assert.IsFalse(harness.Input.IsTextBoxFocused, "Focus lets go of a text box that went back to its pool.");

        harness.Click(reused);

        Assert.IsTrue(reused.IsFocused);
        Assert.IsTrue(harness.Input.IsTextBoxFocused);
    }

    private static TextBox AddTextBox(ControlTestHarness harness, Window window)
    {
        var textBox = harness.Pool.CreateElement<TextBox>(window, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(10, 10), Size = new Vector2(150, 24), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
        });
        window.AddChild(textBox);
        return textBox;
    }

    [TestMethod]
    public void TheActiveTab_IsUpdatedOnceAFrame_AndAHiddenTabNotAtAll()
    {
        var (harness, _, first, second) = Build();

        harness.Frame();
        harness.Frame();

        Assert.AreEqual(2, first.Updates);
        Assert.AreEqual(0, second.Updates);
    }
}
