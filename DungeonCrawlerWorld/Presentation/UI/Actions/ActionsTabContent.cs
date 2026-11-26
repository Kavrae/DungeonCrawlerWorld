using Microsoft.Xna.Framework;
using Presentation.UI.Content;
using UIEngine.UI;
using UIEngine.UI.Content;

namespace Presentation.UI.Actions;

/// <summary>One Actions window tab: its ActionGridContent in a scrolling window, driven by the GridControlToolbar every tab of the window shares.</summary>
/// <remarks>
/// As InventoryTabContent, with one toggle, "Card view": actions never stack. A tab takes the toolbar's current sort and search as it
/// opens and follows them while it is shown, so switching tabs keeps what was typed and the search box keeps focus.
/// </remarks>
public sealed class ActionsTabContent(ElementPoolService elementPoolService, ActionGridContent gridContent, GridControlToolbar toolbar, GridViewSetting viewSetting) : IElementContent
{
    /// <summary>The sort labels the toolbar shows, by ActionSortOrder.</summary>
    public static readonly IReadOnlyList<string> SortOptionLabels = ["A-Z", "Z-A"];

    public const string SearchGhostText = "Search Actions";

    /// <summary>The toolbar's one toggle, "Card view" (index 0), on when viewSetting -- the Actions window's, held for the session -- shows cards.</summary>
    public static IReadOnlyList<(string Label, bool DefaultOn)> TogglesFor(GridViewSetting viewSetting) => [(InventoryTabContent.CardViewToggleLabel, viewSetting.ShowsCards)];

    private Window _hostWindow = null!;
    private Window _gridWindow = null!;

    /// <summary>The grid this tab shows.</summary>
    public ActionGridContent Grid => gridContent;

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
        gridContent.SortOrder = (ActionSortOrder)toolbar.SortOptionIndex;
        gridContent.NameFilter = toolbar.AppliedSearchText;
        toolbar.SetItemCount(gridContent.VisibleItemCount);

        toolbar.SortOptionCycled += OnSortOptionCycled;
        toolbar.SearchFilterChanged += OnSearchFilterChanged;
        toolbar.ToggleChanged += OnToggleChanged;
        hostWindow.Resized += OnHostWindowResized;
    }

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

    private void OnSortOptionCycled(int index) => gridContent.SortOrder = (ActionSortOrder)index;

    private void OnSearchFilterChanged(string text) => gridContent.NameFilter = text;

    private void OnToggleChanged(int index, bool isOn) => viewSetting.Choose(isOn ? GridView.Card : GridView.Icon);

    private void OnHostWindowResized(Element _) => _gridWindow.SetSize(_hostWindow.ContentSize);
}
