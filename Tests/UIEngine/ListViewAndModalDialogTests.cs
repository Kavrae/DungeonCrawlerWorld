using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class ListViewAndModalDialogTests
{
    private static readonly ListViewColumn[] Columns = [new("Name", 120), new("Count", 80), new("Note", 100, Sortable: false)];

    private static readonly ListViewRow[] Rows =
    [
        new(["beta", "10", "x"]),
        new(["Alpha", "9", "y"], "!", Color.Red),
        new(["gamma", "100", "z"]),
    ];

    private static ListView AddList(ControlTestHarness harness, IReadOnlyList<ListViewRow>? rows = null) =>
        harness.AddRoot<ListView>(new Vector2(100, 100), new Vector2(400, 200), list =>
        {
            list.SetColumns(Columns, showIconColumn: true);
            list.SetRows(rows ?? Rows);
        });

    private static Point HeaderOf(ListView list, int columnIndex)
    {
        var x = ListView.IconColumnWidth + Columns.Take(columnIndex).Sum(column => column.Width) + 10;
        return new Point(list.ContentRectangle.X + (int)x, list.ContentRectangle.Y + (int)(ListView.HeaderHeight / 2));
    }

    private static Point RowAt(ListView list, int displayIndex) =>
        new(list.ContentRectangle.X + 60, list.ContentRectangle.Y + (int)(ListView.HeaderHeight + displayIndex * ListView.RowHeight + ListView.RowHeight / 2));

    private static string[] FirstCells(ListView list) => [.. list.RowsInDisplayOrder.Select(row => row.Cells[0])];

    [TestMethod]
    public void ClickingAHeader_SortsByIt_AndAgainReversesIt()
    {
        var harness = new ControlTestHarness();
        var list = AddList(harness);

        harness.Click(HeaderOf(list, 0));
        CollectionAssert.AreEqual(new[] { "Alpha", "beta", "gamma" }, FirstCells(list), "Text sorts ignoring case.");

        harness.Click(HeaderOf(list, 0));
        CollectionAssert.AreEqual(new[] { "gamma", "beta", "Alpha" }, FirstCells(list));

        harness.Click(HeaderOf(list, 1));
        CollectionAssert.AreEqual(new[] { "Alpha", "beta", "gamma" }, FirstCells(list), "Numbers sort as numbers: 9, 10, 100.");

        harness.Click(HeaderOf(list, 2));
        Assert.AreEqual(1, list.SortColumnIndex, "A column that isn't sortable leaves the sort alone.");
    }

    [TestMethod]
    public void AClickSelects_ASecondClickActivates_AndTheSelectionSurvivesASort()
    {
        var harness = new ControlTestHarness();
        var list = AddList(harness);
        var activated = new List<ListViewRow>();
        list.RowActivated += activated.Add;

        harness.Click(RowAt(list, 1));
        Assert.AreSame(Rows[1], list.SelectedRow);
        Assert.IsEmpty(activated);

        harness.Click(RowAt(list, 1));
        CollectionAssert.AreEqual(new[] { Rows[1] }, activated);

        harness.Click(HeaderOf(list, 0));
        Assert.AreSame(Rows[1], list.SelectedRow);
    }

    [TestMethod]
    public void TheKeyboard_MovesTheSelection_AndEnterActivates()
    {
        var harness = new ControlTestHarness();
        var list = AddList(harness);
        var activated = new List<ListViewRow>();
        list.RowActivated += activated.Add;
        harness.Click(RowAt(list, 0));

        harness.Press(Keys.Down);
        harness.Press(Keys.Down);
        harness.Press(Keys.Down);
        Assert.AreSame(Rows[2], list.SelectedRow, "Stops at the last row.");

        harness.Press(Keys.Home);
        Assert.AreSame(Rows[0], list.SelectedRow);

        harness.Press(Keys.Enter);
        CollectionAssert.AreEqual(new[] { Rows[0] }, activated);
    }

    [TestMethod]
    public void NewRows_KeepTheSelection_OnlyWhileTheyHoldIt()
    {
        var harness = new ControlTestHarness();
        var list = AddList(harness);
        harness.Click(RowAt(list, 0));
        ListViewRow? lastSelection = Rows[0];
        list.SelectionChanged += row => lastSelection = row;

        list.SetRows([Rows[2], Rows[0]]);
        Assert.AreSame(Rows[0], list.SelectedRow);

        list.SetRows([Rows[2]]);
        Assert.IsNull(list.SelectedRow);
        Assert.IsNull(lastSelection);
    }

    [TestMethod]
    public void ManyRows_Scroll_AndSelectingOneOffScreen_ScrollsItIntoView()
    {
        var harness = new ControlTestHarness();
        var rows = Enumerable.Range(0, 100).Select(index => new ListViewRow([$"Row {index}", index.ToString(), ""])).ToArray();
        var list = AddList(harness, rows);
        Assert.IsTrue(list.ShowVerticalScrollbar);

        list.Select(rows[99]);

        Assert.AreEqual(list.MaxScrollOffset.Y, list.ScrollOffset.Y);
    }

    private static ModalDialog ShowDialog(ControlTestHarness harness, List<string> answers) =>
        ModalDialog.Show(harness.Pool, harness.Layers, ControlTestHarness.ScreenBounds, new ModalDialogRequest(
            "Disable record",
            [new DialogButton("Cascade", () => answers.Add("Cascade")), new DialogButton("Cancel", () => answers.Add("Cancel"))],
            CancelButtonIndex: 1)
        {
            Message = "Three records use this one.",
        });

    [TestMethod]
    public void ADialog_DimsEverythingElse_AndTheChosenButtonRunsOnce()
    {
        var harness = new ControlTestHarness();
        var behind = AddList(harness);
        var answers = new List<string>();
        var dialog = ShowDialog(harness, answers);

        Assert.IsTrue(harness.Layers.IsMenuModeActive);
        harness.Click(RowAt(behind, 0));
        Assert.IsNull(behind.SelectedRow, "Nothing behind the dialog can be reached.");

        harness.Click(dialog.Buttons[0]);

        CollectionAssert.AreEqual(new[] { "Cascade" }, answers);
        Assert.IsFalse(harness.Layers.IsMenuModeActive);
        Assert.IsFalse(harness.Layers.Contains(UiLayer.DynamicHud, dialog));
    }

    [TestMethod]
    public void Escape_ClosesTheDialog_AsItsCancelButton()
    {
        var harness = new ControlTestHarness();
        var answers = new List<string>();
        ShowDialog(harness, answers);

        harness.Press(Keys.Escape);

        CollectionAssert.AreEqual(new[] { "Cancel" }, answers);
        Assert.IsFalse(harness.Layers.IsMenuModeActive);
    }
}
