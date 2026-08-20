using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.Content;

/// <summary>
/// Composes one GridControl above an InventoryGridContent for a single Inventory tab -- wires
/// each Toggle's own onToggled delegate straight to InventoryGridContent's HideDisabled/
/// GroupDivergedStacks properties at Configure time, translates GridControl's still-generic
/// SortOptionCycled/SearchFilterChanged events into SortOrder/NameFilter, and pushes
/// VisibleItemCount back into GridControl's count display every Update. GridControl itself has no
/// idea any of this is about items -- see its own doc comment. Mirrors InventoryGridContent's own
/// reuse lifecycle: TabbedContent holds one
/// instance per tab and cycles it through repeated Deactivate/Initialize as the player switches
/// tabs back and forth, so Deactivate must fully tear down (and unsubscribe from) whatever
/// Initialize built, the same discipline InventoryGridContent/TabbedContent's own tab tiles
/// already follow.
/// </summary>
public sealed class InventoryTabContent(ElementPoolService elementPoolService, FontService fontService, GlyphRenderer glyphRenderer, InventoryGridContent gridContent) : IElementContent
{
    private static readonly IReadOnlyList<string> SortOptionLabels = ["A-Z", "Z-A", "Qty Hi", "Qty Lo"];

    private const string SearchGhostText = "Search Items";

    private Window _hostWindow = null!;
    private GridControl _gridControl = null!;
    private Window _gridWindow = null!;

    /// <summary>
    /// hostWindow's own children are already guaranteed empty by the time this runs --
    /// Window.SetContent (which always precedes Initialize, see its own doc comment) clears them
    /// defensively as the single choke point for that. Only the Resized subscription needs its
    /// own defensive unsubscribe here, since hostWindow itself is never closed between tab
    /// activations (TabbedContent owns and reuses it across every tab), so nothing else would
    /// ever clear a leftover one.
    /// </summary>
    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;

        hostWindow.Resized -= OnHostWindowResized;

        _gridControl = elementPoolService.CreateElement<GridControl>(hostWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(hostWindow.ContentSize.X, GridControl.RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
        });
        _gridControl.Configure(
            SortOptionLabels,
            [
                ("Hide Disabled", true, isOn => gridContent.HideDisabled = isOn),
                ("Stack Diverged", true, isOn => gridContent.GroupDivergedStacks = isOn),
            ],
            SearchGhostText);
        hostWindow.AddChild(_gridControl); // Already initializes _gridControl -- see AddChild's own doc comment.

        _gridControl.SortOptionCycled += OnSortOptionCycled;
        _gridControl.SearchFilterChanged += OnSearchFilterChanged;

        _gridWindow = elementPoolService.CreateElement<Window>(hostWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = new Vector2(0, GridControl.RowHeight),
                Size = hostWindow.ContentSize - new Vector2(0, GridControl.RowHeight),
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
        });
        hostWindow.AddChild(_gridWindow); // Already initializes _gridWindow -- see AddChild's own doc comment.

        gridContent.Initialize(_gridWindow);
        _gridControl.SetItemCount(gridContent.VisibleItemCount);

        hostWindow.Resized += OnHostWindowResized;
    }

    /// <summary>
    /// _gridControl/_gridWindow are real child Elements of _hostWindow, so their own Update
    /// already runs automatically via _hostWindow's normal children-recursion (see Element.Update)
    /// -- calling it again here would run it twice a frame. gridContent is different: a plain
    /// IElementContent, not an Element in the tree, so nothing else will ever call its Update --
    /// this is the one explicit call this class needs to make (mirrors exactly how TabbedContent
    /// itself explicitly calls _tabs[_activeTabIndex].Content.Update for the same reason).
    /// </summary>
    public void Update(GameTime gameTime)
    {
        gridContent.Update(gameTime);
        _gridControl.SetItemCount(gridContent.VisibleItemCount);
    }

    /// <summary>Nothing to draw directly -- GridControl and the grid window both draw themselves through the normal child-element pass.</summary>
    public void DrawContent(GameTime gameTime)
    {
    }

    /// <summary>
    /// ElementPoolService.CloseElement now recursively closes an element's own children before
    /// returning it to its pool (see its own doc comment) -- so the single CloseAllChildren
    /// (_hostWindow) call below already tears down _gridControl's children (count label,
    /// sort/toggle buttons, search box) and _gridWindow's children (grid cells) as it closes
    /// _gridControl/_gridWindow themselves, clearing every event along the way too. What's still
    /// needed explicitly: gridContent.Deactivate()'s own non-Element bookkeeping (_cells/hover
    /// state), and unsubscribing from _hostWindow.Resized, since _hostWindow isn't being closed
    /// here (TabbedContent owns and reuses it across tabs) -- nothing else would ever clear that.
    /// </summary>
    public void Deactivate()
    {
        gridContent.Deactivate();
        elementPoolService.CloseAllChildren(_hostWindow);
        _hostWindow.Resized -= OnHostWindowResized;
    }

    private void OnSortOptionCycled(int index) => gridContent.SortOrder = (InventorySortOrder)index;

    private void OnSearchFilterChanged(string text) => gridContent.NameFilter = text;

    private void OnHostWindowResized(Element _)
    {
        _gridControl.SetSize(new Vector2(_hostWindow.ContentSize.X, GridControl.RowHeight));
        _gridWindow.SetSize(_hostWindow.ContentSize - new Vector2(0, GridControl.RowHeight));
    }
}
