using Microsoft.Xna.Framework;
using UIEngine;
using UIEngine.UI;
using UIEngine.UI.Chrome;

namespace Presentation.UI.Content;

/// <summary>The hover popup a HUD resource bar owns: shown below the bar after the hover delay, hidden at once on hover loss.</summary>
/// <remarks>
/// The popup window holds no children but is built as a container, so its ContentPadding insets
/// the content it hosts from its border. Its size is the content's own size plus that inset,
/// recomputed only when the content asks for a different size.
/// </remarks>
internal sealed class ResourceBarHoverPopup
{
    /// <summary>The popup sits directly below the bar -- the bar sits in the top-right HUD corner, so above would risk clipping off the top of the screen.</summary>
    private static readonly Vector2 PopupGap = new(0, 2);

    private readonly Window _popupWindow;

    private Vector2 _contentSize;
    private int _hoveredFrames;

    public ResourceBarHoverPopup(Window barWindow, UiLayerStack layers, IElementContent content, Vector2 contentSize)
    {
        _contentSize = contentSize;
        _popupWindow = barWindow.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = Vector2.Zero,
                Size = contentSize,
                DisplayMode = ElementDisplayMode.Fixed,
                IsVisible = false,
            },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Outset, ShowTitle = false, CanUserFocus = false, CanUserClose = false },
        });
        _popupWindow.SetContent(content);
        _popupWindow.Initialize();
        _popupWindow.SetSize(contentSize + _popupWindow.ContentInset);
        layers.Add(UiLayer.Tooltip, _popupWindow);
    }

    public Window Window => _popupWindow;

    /// <summary>Shows the popup below barRectangle once the pointer has been over the bar for the hover delay, sized to contentSize plus its chrome.</summary>
    public void Update(bool isHovering, Rectangle barRectangle, Vector2 contentSize, Rectangle screenBounds)
    {
        _hoveredFrames = isHovering ? _hoveredFrames + 1 : 0;

        if (!isHovering || _hoveredFrames < PopupChrome.HoverTooltipDelayFrames)
        {
            _popupWindow.IsVisible = false;
            return;
        }

        if (contentSize != _contentSize)
        {
            _contentSize = contentSize;
            _popupWindow.SetSize(contentSize + _popupWindow.ContentInset);
        }

        _popupWindow.SetRelativePosition(PopupPositioning.GetPositionWithinBounds(barRectangle, _popupWindow.CurrentSize, PopupAnchor.South, PopupGap, screenBounds));
        _popupWindow.IsVisible = true;
    }
}
