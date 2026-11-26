using Microsoft.Xna.Framework;
using UIEngine.UI;
using UIEngine.UI.Content;

namespace Presentation.UI.Content;

/// <summary>One Inventory tab: its InventoryGridContent in a scrolling window, driven by the GridControlToolbar every tab of the window shares.</summary>
/// <remarks>
/// The toolbar (count, sort, "Stack Diverged", "Card view", search) is the TabbedContent's and survives tab switches, so a tab takes
/// the toolbar's current sort, toggle and search as it opens and follows them while it is shown -- switching tabs keeps
/// what was typed, and the search box keeps focus. TabbedContent keeps one instance per tab and initializes and deactivates
/// it as the player switches tabs, so Deactivate tears down all Initialize built and stops listening to the toolbar.
/// </remarks>
public sealed class InventoryTabContent(ElementPoolService elementPoolService, InventoryGridContent gridContent, GridControlToolbar toolbar, Func<GridViewSetting> viewSetting) : IElementContent
{
    /// <summary>The sort labels the toolbar shows, by InventorySortOrder.</summary>
    public static readonly IReadOnlyList<string> SortOptionLabels = ["A-Z", "Z-A", "Qty Hi", "Qty Lo", "New"];

    /// <summary>The toggles the toolbar shows: "Stack Diverged" (index 0, InventoryGridContent.GroupDivergedStacks), then "Card view" (index 1), on when viewSetting -- the window kind's, held for the session -- shows cards. The window keeps it showing the view in effect, the shop's while a shop is open (InventoryManagementWindow.ViewSettingInEffect), which is also what it switches.</summary>
    public static IReadOnlyList<(string Label, bool DefaultOn)> TogglesFor(GridViewSetting viewSetting) => [("Stack Diverged", true), (CardViewToggleLabel, viewSetting.ShowsCards)];

    /// <summary>The switch between Icon and Card views (GridView), on every grid window's toolbar.</summary>
    public const string CardViewToggleLabel = "Card view";

    public const string SearchGhostText = "Search Items";

    private const int StackDivergedToggleIndex = 0;

    /// <summary>"Card view"'s index among the toolbar's toggles.</summary>
    public const int CardViewToggleIndex = 1;

    private Window _hostWindow = null!;
    private Window _gridWindow = null!;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        hostWindow.Resized -= OnHostWindowResized;

        _gridWindow = elementPoolService.CreateElement<Window>(hostWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = hostWindow.ContentSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        hostWindow.AddChild(_gridWindow);

        gridContent.Initialize(_gridWindow);
        gridContent.SortOrder = (InventorySortOrder)toolbar.SortOptionIndex;
        gridContent.GroupDivergedStacks = toolbar.IsToggleOn(StackDivergedToggleIndex);
        gridContent.NameFilter = toolbar.AppliedSearchText;
        toolbar.SetItemCount(gridContent.VisibleItemCount);

        toolbar.SortOptionCycled += OnSortOptionCycled;
        toolbar.SearchFilterChanged += OnSearchFilterChanged;
        toolbar.ToggleChanged += OnToggleChanged;
        hostWindow.Resized += OnHostWindowResized;
    }

    /// <summary>The grid is a plain IElementContent, not an Element in the tree, so nothing else calls its Update.</summary>
    public void Update(GameTime gameTime)
    {
        gridContent.Update(gameTime);
        toolbar.SetItemCount(gridContent.VisibleItemCount);
    }

    public void DrawContent(GameTime gameTime)
    {
    }

    public void Deactivate()
    {
        toolbar.SortOptionCycled -= OnSortOptionCycled;
        toolbar.SearchFilterChanged -= OnSearchFilterChanged;
        toolbar.ToggleChanged -= OnToggleChanged;
        gridContent.Deactivate();
        elementPoolService.CloseAllChildren(_hostWindow);
        _hostWindow.Resized -= OnHostWindowResized;
    }

    private void OnSortOptionCycled(int index) => gridContent.SortOrder = (InventorySortOrder)index;

    private void OnSearchFilterChanged(string text) => gridContent.NameFilter = text;

    private void OnToggleChanged(int index, bool isOn)
    {
        if (index == StackDivergedToggleIndex)
        {
            gridContent.GroupDivergedStacks = isOn;
        }

        if (index == CardViewToggleIndex)
        {
            viewSetting().Choose(isOn ? GridView.Card : GridView.Icon);
        }
    }

    private void OnHostWindowResized(Element _) => _gridWindow.SetSize(_hostWindow.ContentSize);
}
