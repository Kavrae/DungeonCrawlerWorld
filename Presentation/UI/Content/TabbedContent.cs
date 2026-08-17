using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI.Content;

/// <summary>
/// Horizontally-scrollable tab strip below the host window's title bar, with one shared
/// scrolling body window beneath it whose IElementContent is swapped on tab switch (see
/// SwitchTab) rather than keeping one child window per tab alive forever -- Element.Update
/// recurses into every child regardless of IsVisible, so a one-window-per-tab design would tick
/// every tab ever viewed on every frame. Each tab header is a real child TextWindow tile (not a
/// hand-drawn Rectangle) inside a CanUserScrollHorizontal header Window -- real children get
/// scrolling, click routing, and absolute-position tracking for free from the normal Element
/// pipeline, rather than each needing its own bespoke version (see the git history for the
/// former flat-Rectangle approach, which supported neither scrolling nor a runtime-changeable
/// tab list). One gotcha that free click routing brings along: UiInputController.RaiseToFront
/// reorders whatever tile was just clicked to the end of _tabHeaderWindow's own child list (pure
/// draw/hit-test z-order, the same thing every other clickable Element gets) -- so tab-index
/// bookkeeping here is never allowed to read positions back out of ChildElements, only out of
/// _headerTiles, which this class alone controls the order of. The selected tile's BorderStyle
/// is Outset, every other tile's is Inset. The tab list itself is rebuildable at runtime via
/// SetTabs -- the Inventory window's per-tag tabs need to regenerate whenever the entity's
/// inventory tag composition changes, not just once at construction.
/// </summary>
public sealed class TabbedContent(IReadOnlyList<TabbedContent.TabDefinition> tabs, ElementPoolService elementPoolService, FontService fontService, GlyphRenderer glyphRenderer, Color? bodyBackgroundColor = null) : IElementContent
{
    public sealed record TabDefinition(string Label, IElementContent Content);

    private const float TabHeaderHeight = 28f;
    private const float TabHorizontalPadding = 14f;

    private static readonly Color TabTileColor = new(48, 48, 48);
    private static readonly Color TabLabelColor = Color.White;

    private IReadOnlyList<TabDefinition> _tabs = tabs;

    /// <summary>
    /// Tiles in _tabs order -- NOT the same as _tabHeaderWindow.ChildElements, whose order
    /// UiInputController.RaiseToFront mutates on every click (it moves whatever was just clicked
    /// to the end of its parent's child list, purely for draw/hit-test z-order). Indexing into
    /// ChildElements by tab index used to desync from the actual selected tab after the very
    /// first click for exactly that reason -- this list is rebuilt fresh alongside the tiles and
    /// never reordered afterward, so index i here always means "tab i" regardless of click order.
    /// </summary>
    private readonly List<TextWindow> _headerTiles = [];

    /// <summary>The Clicked delegate registered for each _headerTiles entry, kept so RebuildHeaderTiles can unsubscribe it before returning a tile to ElementPoolService's shared TextWindow pool -- pool reuse never clears event subscriptions on its own (see Detach's own doc comment for the same reasoning applied to Resized), so without this a tile rented for a later rebuild would still carry every earlier rebuild's stale handler and fire all of them -- each with a different captured tab index -- on a single click.</summary>
    private readonly List<Action<Element>> _headerTileClickHandlers = [];

    private Window _hostWindow = null!;
    private Window _tabHeaderWindow = null!;
    private Window _bodyWindow = null!;
    private SpriteFontBase _font = null!;
    private int _activeTabIndex = -1;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _font = fontService.GetFont((int)(TabHeaderHeight * 0.6f));

        _tabHeaderWindow = elementPoolService.CreateElement<Window>(hostWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(hostWindow.ContentSize.X, TabHeaderHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollHorizontal = true, CanUserFocus = false },
        });
        hostWindow.AddChild(_tabHeaderWindow);
        _tabHeaderWindow.Initialize();

        _bodyWindow = elementPoolService.CreateElement<Window>(hostWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = new Vector2(0, TabHeaderHeight),
                Size = hostWindow.ContentSize - new Vector2(0, TabHeaderHeight),
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = bodyBackgroundColor ?? Color.White },
        });
        hostWindow.AddChild(_bodyWindow);
        _bodyWindow.Initialize();

        hostWindow.Resized += OnHostWindowResized;

        RebuildHeaderTiles();
        SwitchTab(0);
    }

    /// <summary>
    /// Unsubscribes from the host window's events -- needed because ElementPoolService never
    /// clears event subscriptions on pool reuse (see NotificationCenter.OnActiveNotificationClosed
    /// for the same reasoning applied to Window.Closed): InventoryManagementWindow is a pooled,
    /// reused Window, and Configure builds a brand new TabbedContent on every open, so without
    /// this the previous open's now-discarded instance would keep reacting to Resized
    /// alongside the current one. Call before replacing/discarding a TabbedContent bound to a
    /// window instance that might be reused later.
    /// </summary>
    public void Detach() => _hostWindow.Resized -= OnHostWindowResized;

    public void Update(GameTime gameTime) => _tabs[_activeTabIndex].Content.Update(gameTime);

    /// <summary>Nothing to draw directly -- the tab strip is real child TextWindow tiles now (drawn through the normal child-element pass), and the active tab's own content renders the same way through _bodyWindow.</summary>
    public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
    }

    /// <summary>
    /// Replaces the tab list wholesale and rebuilds the header strip -- the Inventory window's
    /// per-tag tabs call this whenever the entity's inventory tag composition changes (see
    /// InventoryManagementWindow's own version-watch). Every TabDefinition here is a fresh
    /// instance (even one representing "the same tab" conceptually, e.g. "All") since the tag
    /// counts driving the whole list may have shifted -- so this always deactivates whatever was
    /// active under the old list and initializes fresh under the new one, rather than trying to
    /// detect "did this specific tab's content actually change." Selection is preserved by
    /// matching the previously-active tab's Label against the new list; if no tab with that
    /// label exists anymore (e.g. the last item of that tag was just removed), falls back to
    /// index 0 ("All").
    /// </summary>
    public void SetTabs(IReadOnlyList<TabDefinition> newTabs)
    {
        var previousLabel = _activeTabIndex >= 0 ? _tabs[_activeTabIndex].Label : null;

        if (_activeTabIndex >= 0)
        {
            _tabs[_activeTabIndex].Content.Deactivate();
        }

        _tabs = newTabs;
        _activeTabIndex = -1;
        RebuildHeaderTiles();

        var newIndex = previousLabel is not null ? IndexOfLabel(previousLabel) : -1;
        SwitchTab(newIndex >= 0 ? newIndex : 0);
    }

    private int IndexOfLabel(string label)
    {
        for (var i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].Label == label)
            {
                return i;
            }
        }

        return -1;
    }

    private void SwitchTab(int index)
    {
        if (index == _activeTabIndex)
        {
            return;
        }

        if (_activeTabIndex >= 0)
        {
            _tabs[_activeTabIndex].Content.Deactivate();
        }

        _activeTabIndex = index;
        _bodyWindow.SetContent(_tabs[index].Content);
        _tabs[index].Content.Initialize(_bodyWindow);
        UpdateHeaderSelectionVisuals();
    }

    private void UpdateHeaderSelectionVisuals()
    {
        for (var i = 0; i < _headerTiles.Count; i++)
        {
            _headerTiles[i].BorderStyle = i == _activeTabIndex ? BorderStyle.Outset : BorderStyle.Inset;
        }
    }

    private void OnHostWindowResized(Element _)
    {
        _tabHeaderWindow.SetSize(new Vector2(_hostWindow.ContentSize.X, TabHeaderHeight));
        _bodyWindow.SetSize(_hostWindow.ContentSize - new Vector2(0, TabHeaderHeight));
    }

    private void RebuildHeaderTiles()
    {
        for (var i = 0; i < _headerTiles.Count; i++)
        {
            _headerTiles[i].Clicked -= _headerTileClickHandlers[i];
        }

        elementPoolService.CloseAllChildren(_tabHeaderWindow);
        _headerTiles.Clear();
        _headerTileClickHandlers.Clear();

        var x = 0f;
        for (var i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            var width = _font.MeasureString(tab.Label).X + TabHorizontalPadding * 2;

            var tile = elementPoolService.CreateElement<TextWindow>(_tabHeaderWindow, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(x, 0), Size = new Vector2(width, TabHeaderHeight), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Inset, ShowTitle = false, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = TabTileColor },
                Text = new TextOptions { Text = tab.Label, TextColor = TabLabelColor },
            });
            tile.ContentFont = _font; // Must match the font width was measured with above, or the label can wrap/clip against the tile's own fixed width.
            _tabHeaderWindow.AddChild(tile);

            var capturedIndex = i;
            Action<Element> clickHandler = _ => SwitchTab(capturedIndex);
            tile.Clicked += clickHandler;

            _headerTiles.Add(tile);
            _headerTileClickHandlers.Add(clickHandler);

            x += width;
        }
    }
}
