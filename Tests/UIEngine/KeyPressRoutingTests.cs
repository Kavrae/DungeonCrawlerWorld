using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class KeyPressRoutingTests
{
    [TestMethod]
    [DataRow(Keys.Down, 2, 3)]
    [DataRow(Keys.Up, 2, 1)]
    [DataRow(Keys.PageDown, 2, 6)]
    [DataRow(Keys.PageUp, 6, 2)]
    [DataRow(Keys.PageDown, 8, 9)]
    [DataRow(Keys.PageUp, 1, 0)]
    [DataRow(Keys.Home, 5, 0)]
    [DataRow(Keys.End, 5, 9)]
    [DataRow(Keys.Down, 9, 9)]
    [DataRow(Keys.Up, 0, 0)]
    public void ListMoves_StepRowsAndPages_StoppingAtEitherEnd(Keys key, int selectedIndex, int expected)
    {
        Assert.IsTrue(ListKeyNavigation.TryMove(key, selectedIndex, count: 10, rowsPerPage: 4, out var target));
        Assert.AreEqual(expected, target);
    }

    [TestMethod]
    [DataRow(Keys.Down, 0)]
    [DataRow(Keys.PageDown, 0)]
    [DataRow(Keys.Home, 0)]
    [DataRow(Keys.Up, 9)]
    [DataRow(Keys.PageUp, 9)]
    [DataRow(Keys.End, 9)]
    public void ListMoves_FromNoSelection_GoToTheEndTheyFace(Keys key, int expected)
    {
        Assert.IsTrue(ListKeyNavigation.TryMove(key, -1, count: 10, rowsPerPage: 4, out var target));
        Assert.AreEqual(expected, target);
    }

    [TestMethod]
    public void ListMoves_TakeOnlyTheirKeys_AndSelectNothingInAnEmptyList()
    {
        Assert.IsFalse(ListKeyNavigation.TryMove(Keys.Delete, 0, 10, 4, out _));
        Assert.IsTrue(ListKeyNavigation.TryMove(Keys.Down, -1, 0, 4, out var target));
        Assert.AreEqual(-1, target);
    }

    private static (ControlTestHarness Harness, Window Root, List<Keys> RootKeys) RootWithKeys()
    {
        var harness = new ControlTestHarness();
        var rootKeys = new List<Keys>();
        var root = harness.AddRoot<Window>(new Vector2(100, 100), new Vector2(400, 300));
        root.OnKeyPressed = key =>
        {
            rootKeys.Add(key);
            return true;
        };
        return (harness, root, rootKeys);
    }

    private static T AddChild<T>(ControlTestHarness harness, Window root, float y)
        where T : Element
    {
        var child = harness.Pool.CreateElement<T>(root, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(10, y), Size = new Vector2(200, 24), DisplayMode = ElementDisplayMode.Fixed },
        });
        root.AddChild(child);
        return child;
    }

    [TestMethod]
    public void AKeyTheFocusedControlDoesNotTake_GoesToItsAncestors()
    {
        var (harness, root, rootKeys) = RootWithKeys();
        var toggle = AddChild<Toggle>(harness, root, 10);
        var toggled = 0;
        toggle.Configure(false, Color.Green, Color.Gray, _ => toggled++);
        harness.Input.FocusElement(toggle);

        harness.Press(Keys.Delete);
        harness.Press(Keys.Space);

        CollectionAssert.AreEqual(new[] { Keys.Delete }, rootKeys, "Space is the toggle's own, so it goes no further.");
        Assert.AreEqual(1, toggled);
    }

    [TestMethod]
    public void AFocusedTextBox_KeepsEveryKey()
    {
        var (harness, root, rootKeys) = RootWithKeys();
        var textBox = AddChild<TextBox>(harness, root, 10);
        harness.Input.FocusElement(textBox);

        harness.Press(Keys.Delete);
        harness.Press(Keys.Home);
        harness.Press(Keys.Up);

        Assert.IsEmpty(rootKeys);
    }

    [TestMethod]
    public void WhileADropdownListIsOpen_NoKeyGoesPastTheFocusedControl()
    {
        var (harness, root, rootKeys) = RootWithKeys();
        var dropdown = AddChild<DropdownField>(harness, root, 10);
        dropdown.SetOptions(["One", "Two"], 0);
        harness.Input.FocusElement(dropdown);

        harness.Press(Keys.Space);
        Assert.IsTrue(dropdown.IsListOpen);
        harness.Press(Keys.Delete);

        Assert.IsEmpty(rootKeys);
    }

    [TestMethod]
    public void AListView_PagesByTheRowsItShows_AndKeepsItsMoves()
    {
        var (harness, root, rootKeys) = RootWithKeys();
        var list = harness.Pool.CreateElement<ListView>(root, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(300, ListView.HeaderHeight + ListView.RowHeight * 4), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false },
        });
        root.AddChild(list);
        list.SetColumns([new ListViewColumn("Name", 120)], showIconColumn: false);
        ListViewRow[] rows = [.. Enumerable.Range(0, 20).Select(index => new ListViewRow([$"Row {index}"]))];
        list.SetRows(rows);
        harness.Frame();
        harness.Input.FocusElement(list);

        harness.Press(Keys.Down);
        Assert.AreSame(rows[0], list.SelectedRow);
        harness.Press(Keys.PageDown);
        Assert.AreSame(rows[4], list.SelectedRow);
        harness.Press(Keys.End);
        Assert.AreSame(rows[19], list.SelectedRow);
        harness.Press(Keys.PageUp);
        Assert.AreSame(rows[15], list.SelectedRow);
        harness.Press(Keys.Delete);

        CollectionAssert.AreEqual(new[] { Keys.Delete }, rootKeys, "Only what the list doesn't take goes on.");
    }
}
