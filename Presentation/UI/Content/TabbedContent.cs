using Engine.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;

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
///
/// A right-aligned search box shares the tab row (see _searchBox), debounce-filtering which tab
/// headers are visible by a case-insensitive Label.Contains match -- "All" (index 0) always
/// stays visible regardless of the filter. Filtering only ever changes which header tiles exist;
/// _tabs/_bodyWindow/_activeTabIndex are untouched, so the active tab's content keeps showing
/// even if a search happens to hide its own header tile. Since a filtered tab list is no longer
/// a contiguous, same-order subset of _tabs, _headerTiles now carries each visible tile's real
/// _tabs index alongside it, rather than relying on its own list position to mean that index.
/// </summary>
public sealed class TabbedContent(IReadOnlyList<TabbedContent.TabDefinition> tabs, ElementPoolService elementPoolService, FontService fontService, GlyphRenderer glyphRenderer, Color? bodyBackgroundColor = null) : IElementContent
{
    public sealed record TabDefinition(string Label, IElementContent Content);

    private const float TabHeaderHeight = 28f;
    private const float TabHorizontalPadding = 9f;

    private const float SearchBoxWidth = 112f;
    private const float SearchBoxGap = 4f;
    private const string SearchGhostText = "Search Tabs";

    /// <summary>How long the search box's text must sit unchanged before it's applied as a filter -- 300ms.</summary>
    private static readonly int SearchDebounceFrames = GameTiming.FramesForSeconds(0.3f);

    private static readonly Color TabTileColor = new(48, 48, 48);
    private static readonly Color TabLabelColor = Color.White;

    private static readonly Color SearchBoxColor = new(WindowPalette.PanelBackgroundColor.R / 2, WindowPalette.PanelBackgroundColor.G / 2, WindowPalette.PanelBackgroundColor.B / 2);

    private IReadOnlyList<TabDefinition> _tabs = tabs;

    /// <summary>
    /// Currently-visible header tiles, each paired with the real _tabs index it represents --
    /// NOT the same as _tabHeaderWindow.ChildElements, whose order UiInputController.RaiseToFront
    /// mutates on every click (it moves whatever was just clicked to the end of its parent's
    /// child list, purely for draw/hit-test z-order), and not necessarily a contiguous run of
    /// _tabs either, since the search filter (see ApplySearchFilter) can skip entries. This list
    /// is rebuilt fresh alongside the tiles and never reordered afterward, so TabIndex is always
    /// the one true source for "which tab does this tile activate."
    /// </summary>
    private readonly List<(int TabIndex, TextWindow Tile, Action<Element> ClickHandler)> _headerTiles = [];

    private Window _hostWindow = null!;
    private Window _tabHeaderWindow = null!;
    private Window _bodyWindow = null!;
    private TextBox _searchBox = null!;
    private SpriteFontBase _font = null!;
    private int _activeTabIndex = -1;

    private string _lastSeenSearchText = string.Empty;
    private int _searchDebounceFrames;
    private string _appliedSearchFilter = string.Empty;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _font = fontService.GetFont((int)(TabHeaderHeight * 0.6f));

        _tabHeaderWindow = elementPoolService.CreateElement<Window>(hostWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(HeaderStripWidth(hostWindow.ContentSize.X), TabHeaderHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollHorizontal = true, CanUserFocus = false },
        });
        hostWindow.AddChild(_tabHeaderWindow);
        _tabHeaderWindow.Initialize();

        _searchBox = elementPoolService.CreateElement<TextBox>(hostWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = SearchBoxPosition(hostWindow.ContentSize.X), Size = new Vector2(SearchBoxWidth, TabHeaderHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false },
            Content = new ElementContentOptions { ContentColor = SearchBoxColor },
            Text = new TextOptions { TextColor = TabLabelColor },
        });
        _searchBox.ContentFont = _font;
        _searchBox.GhostText = SearchGhostText;
        _searchBox.GhostTextColor = Color.LightGray;
        hostWindow.AddChild(_searchBox);
        _searchBox.Initialize();

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

    public void Update(GameTime gameTime)
    {
        _tabs[_activeTabIndex].Content.Update(gameTime);
        UpdateSearchFilterDebounce();
    }

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
    /// index 0 ("All"). The currently-applied search filter (if any) carries over unchanged --
    /// picking up an item mid-search shouldn't clear what was typed.
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
        foreach (var (tabIndex, tile, _) in _headerTiles)
        {
            tile.BorderStyle = tabIndex == _activeTabIndex ? BorderStyle.Outset : BorderStyle.Inset;
        }
    }

    /// <summary>
    /// Polls the search box's own text once per frame rather than reacting to a text-changed
    /// event -- TextBox has no such event (it mutates OriginalText directly from
    /// OnTextInputAction/OnKeyPressAction), and polling is cheap/simple enough here that adding
    /// one wasn't worth it. Resets the debounce counter on any change; once the text has sat
    /// still for SearchDebounceFrames and isn't already the applied filter, applies it exactly
    /// once (the _lastSeenSearchText == _appliedSearchFilter check is what stops this from
    /// re-applying every frame after that).
    /// </summary>
    private void UpdateSearchFilterDebounce()
    {
        var currentText = _searchBox.OriginalText;
        if (currentText != _lastSeenSearchText)
        {
            _lastSeenSearchText = currentText;
            _searchDebounceFrames = 0;
            return;
        }

        if (currentText == _appliedSearchFilter)
        {
            return;
        }

        _searchDebounceFrames++;
        if (_searchDebounceFrames >= SearchDebounceFrames)
        {
            ApplySearchFilter(currentText);
        }
    }

    private void ApplySearchFilter(string filterText)
    {
        _appliedSearchFilter = filterText;
        RebuildHeaderTiles();
        UpdateHeaderSelectionVisuals();
    }

    private void OnHostWindowResized(Element _)
    {
        _tabHeaderWindow.SetSize(new Vector2(HeaderStripWidth(_hostWindow.ContentSize.X), TabHeaderHeight));
        _searchBox.SetRelativePosition(SearchBoxPosition(_hostWindow.ContentSize.X));
        _bodyWindow.SetSize(_hostWindow.ContentSize - new Vector2(0, TabHeaderHeight));
    }

    private static float HeaderStripWidth(float hostContentWidth) => hostContentWidth - SearchBoxWidth - SearchBoxGap;

    private static Vector2 SearchBoxPosition(float hostContentWidth) => new(hostContentWidth - SearchBoxWidth, 0);

    /// <summary>
    /// Tab 0 ("All") is exempt from the filter and always gets a tile -- every other tab only
    /// gets one when _appliedSearchFilter is empty (no active search) or its Label contains it
    /// (case-insensitive). Filtering only changes which tiles exist here; it never touches _tabs
    /// itself, _bodyWindow's content, or _activeTabIndex -- see this class's own doc comment.
    /// </summary>
    private void RebuildHeaderTiles()
    {
        foreach (var (_, tile, clickHandler) in _headerTiles)
        {
            tile.Clicked -= clickHandler;
        }

        elementPoolService.CloseAllChildren(_tabHeaderWindow);
        _headerTiles.Clear();

        var x = 0f;
        for (var tabIndex = 0; tabIndex < _tabs.Count; tabIndex++)
        {
            var tab = _tabs[tabIndex];
            if (tabIndex != 0 && _appliedSearchFilter.Length > 0 && !tab.Label.Contains(_appliedSearchFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var width = _font.MeasureString(tab.Label).X + TabHorizontalPadding * 2;

            var tileSize = new Vector2(width, TabHeaderHeight);
            var tile = elementPoolService.CreateElement<TextWindow>(_tabHeaderWindow, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(x, 0), Size = tileSize, MinimumSize = tileSize, MaximumSize = tileSize, DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Inset, ShowTitle = false, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = TabTileColor },
                Text = new TextOptions { Text = tab.Label, TextColor = TabLabelColor },
            });
            tile.ContentFont = _font; // Must match the font width was measured with above, or the label can wrap/clip against the tile's own fixed width.
            _tabHeaderWindow.AddChild(tile);

            var capturedIndex = tabIndex;
            Action<Element> clickHandler = _ => SwitchTab(capturedIndex);
            tile.Clicked += clickHandler;

            _headerTiles.Add((tabIndex, tile, clickHandler));

            x += width;
        }
    }
}
