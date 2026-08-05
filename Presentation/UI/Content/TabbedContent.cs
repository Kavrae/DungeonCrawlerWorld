using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI.Content;

/// <summary>
/// Horizontal tab strip below the host window's title bar, with one shared scrolling body
/// window beneath it whose IElementContent is swapped on tab switch (see SwitchTab) rather than
/// keeping one child window per tab alive forever -- Element.Update recurses into every child
/// regardless of IsVisible, so a one-window-per-tab design would tick every tab ever viewed on
/// every frame. Ships with a single "All" tab today; the per-tab-body-window swap machinery is
/// what a future dynamic per-tag-tabs feature needs without redesigning this class.
/// </summary>
public sealed class TabbedContent(IReadOnlyList<TabbedContent.TabDefinition> tabs, ElementPoolService elementPoolService, FontService fontService, GlyphRenderer glyphRenderer, Color? bodyBackgroundColor = null) : IElementContent
{
    public sealed record TabDefinition(string Label, IElementContent Content);

    private const float TabHeaderHeight = 28f;
    private const float TabHorizontalPadding = 14f;

    private static readonly Color InactiveTabColor = new(48, 48, 48);
    private static readonly Color ActiveTabColor = new(90, 90, 90);
    private static readonly Color TabLabelColor = Color.White;

    private readonly List<Rectangle> _tabRectangles = [];

    private Window _hostWindow = null!;
    private Window _bodyWindow = null!;
    private SpriteFontBase _font = null!;
    private int _activeTabIndex = -1;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _font = fontService.GetFont((int)(TabHeaderHeight * 0.6f));

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
        hostWindow.Moved += OnHostWindowMoved;

        RecomputeTabRectangles();
        SwitchTab(0);
    }

    /// <summary>
    /// Unsubscribes from the host window's events -- needed because ElementPoolService never
    /// clears event subscriptions on pool reuse (see NotificationCenter.OnActiveNotificationClosed
    /// for the same reasoning applied to Window.Closed): InventoryManagementWindow is a pooled,
    /// reused Window, and Configure builds a brand new TabbedContent on every open, so without
    /// this the previous open's now-discarded instance would keep reacting to Resized/Moved
    /// alongside the current one. Call before replacing/discarding a TabbedContent bound to a
    /// window instance that might be reused later.
    /// </summary>
    public void Detach()
    {
        _hostWindow.Resized -= OnHostWindowResized;
        _hostWindow.Moved -= OnHostWindowMoved;
    }

    public void Update(GameTime gameTime) => tabs[_activeTabIndex].Content.Update(gameTime);

    /// <summary>Draws only the tab-header strip -- the active tab's own content renders through the normal child-window draw pass (_bodyWindow).</summary>
    public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        for (var i = 0; i < tabs.Count; i++)
        {
            var rectangle = _tabRectangles[i];
            spriteBatch.Draw(unitRectangle, rectangle, i == _activeTabIndex ? ActiveTabColor : InactiveTabColor);

            var textSize = _font.MeasureString(tabs[i].Label);
            var textPosition = new Vector2(
                rectangle.X + (rectangle.Width - textSize.X) / 2f,
                rectangle.Y + (rectangle.Height - textSize.Y) / 2f);
            spriteBatch.DrawString(_font, tabs[i].Label, textPosition, TabLabelColor);
        }
    }

    /// <summary>Hit-tests the tab-header strip -- called directly by the host window (e.g. InventoryManagementWindow.OnContentClickAction), not routed through IElementContent.</summary>
    public void HandleClick(Point mousePosition)
    {
        for (var i = 0; i < _tabRectangles.Count; i++)
        {
            if (_tabRectangles[i].Contains(mousePosition))
            {
                SwitchTab(i);
                return;
            }
        }
    }

    private void SwitchTab(int index)
    {
        if (index == _activeTabIndex)
        {
            return;
        }

        if (_activeTabIndex >= 0)
        {
            tabs[_activeTabIndex].Content.Deactivate();
        }

        _activeTabIndex = index;
        _bodyWindow.SetContent(tabs[index].Content);
        tabs[index].Content.Initialize(_bodyWindow);
    }

    private void OnHostWindowResized(Element _)
    {
        _bodyWindow.SetSize(_hostWindow.ContentSize - new Vector2(0, TabHeaderHeight));
        RecomputeTabRectangles();
    }

    /// <summary>Tab-header rectangles cache absolute screen positions (see RecomputeTabRectangles), which only Resized used to refresh -- moving the window without resizing it left them stale, so tabs visually stayed behind while the body window (a real child Element, positioned relatively) correctly followed.</summary>
    private void OnHostWindowMoved(Element _) => RecomputeTabRectangles();

    private void RecomputeTabRectangles()
    {
        _tabRectangles.Clear();

        var x = 0f;
        foreach (var tab in tabs)
        {
            var width = _font.MeasureString(tab.Label).X + TabHorizontalPadding * 2;
            _tabRectangles.Add(new Rectangle((int)(_hostWindow.ContentAbsolutePosition.X + x), (int)_hostWindow.ContentAbsolutePosition.Y, (int)width, (int)TabHeaderHeight));
            x += width;
        }
    }
}
