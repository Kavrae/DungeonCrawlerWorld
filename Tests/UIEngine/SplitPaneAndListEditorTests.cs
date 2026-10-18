using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class SplitPaneAndListEditorTests
{
    private static SplitPane AddSplit(ControlTestHarness harness, SplitOrientation orientation, SplitSizedPane sizedPane, float sizedPaneSize) =>
        harness.AddRoot<SplitPane>(new Vector2(0, 0), new Vector2(600, 400), split => split.Configure(orientation, sizedPane, sizedPaneSize, firstPaneMinimumSize: 100, secondPaneMinimumSize: 50), new ElementChromeOptions { ShowBorder = false });

    private static Point SplitterCentre(SplitPane split) => split.Orientation == SplitOrientation.SideBySide
        ? new Point(split.FirstPane.Rectangle.Right + (int)SplitPane.SplitterThickness / 2, split.ContentRectangle.Center.Y)
        : new Point(split.ContentRectangle.Center.X, split.FirstPane.Rectangle.Bottom + (int)SplitPane.SplitterThickness / 2);

    [TestMethod]
    public void DraggingTheSplitter_ResizesBothPanes_AndShowsTheResizeCursor()
    {
        var harness = new ControlTestHarness();
        var split = AddSplit(harness, SplitOrientation.SideBySide, SplitSizedPane.First, 200);
        var splitter = SplitterCentre(split);
        var changes = new List<float>();
        split.SplitChanged += changes.Add;

        harness.MoveTo(splitter);
        Assert.AreEqual(MouseCursor.SizeWE, harness.Input.CurrentCursor);

        harness.Drag(splitter, splitter + new Point(60, 0));

        Assert.AreEqual(260f, split.FirstPane.CurrentSize.X);
        Assert.AreEqual(split.ContentSize.X - 260f - SplitPane.SplitterThickness, split.SecondPane.CurrentSize.X);
        Assert.AreEqual(260f, changes[^1]);
    }

    [TestMethod]
    public void TheSplitter_StopsAtEitherPanesMinimum()
    {
        var harness = new ControlTestHarness();
        var split = AddSplit(harness, SplitOrientation.SideBySide, SplitSizedPane.First, 200);

        harness.Drag(SplitterCentre(split), new Point(5, 200));
        Assert.AreEqual(100f, split.FirstPane.CurrentSize.X);

        harness.Drag(SplitterCentre(split), new Point(599, 200));
        Assert.AreEqual(50f, split.SecondPane.CurrentSize.X);
    }

    [TestMethod]
    public void AStackedSplit_SizedOnItsSecondPane_KeepsThatPanesHeight_WhenItIsResized()
    {
        var harness = new ControlTestHarness();
        var split = AddSplit(harness, SplitOrientation.Stacked, SplitSizedPane.Second, 120);
        Assert.AreEqual(120f, split.SecondPane.CurrentSize.Y);

        harness.Drag(SplitterCentre(split), SplitterCentre(split) - new Point(0, 30));
        Assert.AreEqual(150f, split.SecondPane.CurrentSize.Y, "Dragging up grows the bottom pane.");

        split.SetSize(new Vector2(600, 500));
        Assert.AreEqual(150f, split.SecondPane.CurrentSize.Y);
        Assert.AreEqual(500f - 150f - SplitPane.SplitterThickness, split.FirstPane.CurrentSize.Y);
    }

    [TestMethod]
    public void RowsTallerThanTheEditor_AreBuiltForItsScrollbar_AndTheirContentFitsBesideTheButtons()
    {
        var harness = new ControlTestHarness();
        var editor = harness.AddRoot<ListEditor>(new Vector2(50, 50), new Vector2(300, 100), list => list.Configure(rowHeight: 24));
        void Fill(int count) => editor.SetRows(count, (content, _) =>
        {
            var label = harness.Pool.CreateElement<TextWindow>(content, new ElementOptions
            {
                Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = content.ContentSize, DisplayMode = ElementDisplayMode.Fixed },
            });
            content.AddChild(label);
        });

        Fill(1);
        Assert.IsFalse(editor.ShowVerticalScrollbar);
        Fill(8);

        Assert.IsTrue(editor.ShowVerticalScrollbar);
        var expectedWidth = editor.ContentSize.X - ListEditor.RowButtonWidth * 3 - 2f * 3;
        foreach (var content in editor.RowContents)
        {
            Assert.AreEqual(expectedWidth, content.CurrentSize.X);
            Assert.HasCount(1, content.ChildElements);
            Assert.AreEqual(expectedWidth, content.ChildElements[0].CurrentSize.X);
        }
    }

    [TestMethod]
    public void APooledButton_StartsWithTheDefaultFont_WhateverItsLastUserSet()
    {
        var harness = new ControlTestHarness();
        var first = harness.Pool.CreateElement<Button>(null, new ElementOptions { Layout = new ElementLayoutOptions { Size = new Vector2(20, 20) } });
        var defaultFont = first.ContentFont;
        first.ContentFont = TestFonts.Shared.GetFont(40);
        harness.Pool.CloseElement(first);

        var reused = harness.Pool.CreateElement<Button>(null, new ElementOptions { Layout = new ElementLayoutOptions { Size = new Vector2(20, 20) } });

        Assert.AreSame(first, reused);
        Assert.AreSame(defaultFont, reused.ContentFont);
    }

    [TestMethod]
    public void AListEditorsButtons_AskTheOwner_OnTheUpdateAfterTheClick()
    {
        var harness = new ControlTestHarness();
        var items = new List<string> { "a", "b", "c" };
        ListEditor editor = null!;
        void Fill() => editor.SetRows(items.Count, (content, index) =>
        {
            var label = harness.Pool.CreateElement<TextWindow>(content, new ElementOptions
            {
                Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = content.ContentSize, DisplayMode = ElementDisplayMode.Fixed },
                Text = new TextOptions { Text = items[index] },
            });
            content.AddChild(label);
        });
        editor = harness.AddRoot<ListEditor>(new Vector2(50, 50), new Vector2(300, 200), list => list.Configure(rowHeight: 24));
        editor.RemoveRequested += index => { items.RemoveAt(index); Fill(); };
        editor.MoveRequested += (from, to) => { (items[from], items[to]) = (items[to], items[from]); Fill(); };
        editor.AddRequested += () => { items.Add("new"); Fill(); };
        Fill();

        Button ButtonOf(int row, string label) => editor.ChildElements.OfType<Button>().Where(button => button.LeftText == label).ElementAt(row);
        Assert.IsFalse(ButtonOf(0, ListEditor.MoveUpLabel).Enabled);
        Assert.IsFalse(ButtonOf(2, ListEditor.MoveDownLabel).Enabled);

        harness.Click(ButtonOf(0, ListEditor.MoveDownLabel));
        CollectionAssert.AreEqual(new[] { "b", "a", "c" }, items);

        harness.Click(ButtonOf(2, ListEditor.RemoveLabel));
        CollectionAssert.AreEqual(new[] { "b", "a" }, items);
        Assert.AreEqual(2, editor.RowCount);

        harness.Click(editor.ChildElements.OfType<Button>().Single(button => button.LeftText == "Add"));
        CollectionAssert.AreEqual(new[] { "b", "a", "new" }, items);
    }
}
