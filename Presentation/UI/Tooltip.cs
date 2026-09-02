using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI;

/// <summary>
/// A single persistent, pooled-style TextWindow shown/hidden via IsVisible (rather than
/// created/closed per popup) and positioned relative to whatever triggered it -- one of the 8
/// PopupAnchor compass directions plus a pixel gap, resolved by PopupPositioning. Always a
/// top-level element (parent null), deliberately -- a nested child's own MaximumSize gets
/// silently overwritten every layout pass to (parent.ContentSize - RelativePosition) (see
/// Element.MeasureAndArrange's non-root branch), which is fine for content meant to stay inside
/// its parent but structurally incompatible with a popup that's meant to float outside it (e.g.
/// East of a header/row sitting near the parent's own right edge -- confirmed by AbilityScoreWindow's
/// original nested-child attempt, whose popup width shrank to 0 for any column close enough to
/// the parent's edge that the remaining space ran out). Being top-level sidesteps that entirely
/// -- RelativePosition is already the absolute screen position, and MaximumSize behaves as the
/// stable, position-independent cap it was configured with (see Element.Measure's root branch:
/// availableSize is read from, then written straight back to, MaximumSize -- a no-op).
///
/// Added to UiLayer.Tooltip, not DynamicHud -- Tooltip sits structurally above DynamicHud and
/// below User (see UiLayer's own doc comment), so this always draws over whatever window it's
/// describing without needing to reorder itself against that window's own tier. Before the
/// Tooltip tier existed, every Tooltip-family instance lived in DynamicHud alongside ordinary
/// floating windows and had to re-append itself to the end of that shared list on every ShowNear
/// call just to keep winning draw order against them -- that's gone now; the tier itself
/// guarantees it.
/// </summary>
public sealed class Tooltip(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer)
    : TextWindow(fontService, elementPoolService, labelRenderer)
{
    /// <summary>TextWindow's own default (WindowPalette.BodyTextColor, Black) reads fine against a light content background -- every Tooltip popup instead sits on WindowPalette.PanelBackgroundColor's dark fill (Element.Build's own ContentColor fallback), where black text is illegible. No caller currently opts out with its own explicit TextOptions.TextColor, but this still only overrides the fallback, not an explicit choice.</summary>
    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        TextColor = options.Text?.TextColor ?? WindowPalette.TitleTextColor;
    }

    /// <summary>Repositions and shows this popup next to target -- a titleText bar only if titleText is supplied (toggled dynamically since one shared instance may be used both with and without a titleText across calls, e.g. AbilityScoreWindow's score-description vs. modifier-source popups). Call every frame the same thing should stay hovered -- cheap no-op churn, the same idiom every current caller (AbilityScoreWindow, InventoryGridContent, HotbarController) already uses for its own hover-driven popup.</summary>
    public void ShowNear(Rectangle target, PopupAnchor anchor, Vector2 gap, string bodyText, string? titleText = null)
    {
        _headerState.ShowHeader = titleText is not null;
        TitleText = titleText ?? string.Empty;

        UpdateText(bodyText);
        SetRelativePosition(PopupPositioning.GetPositionWithinBounds(target, CurrentSize, anchor, gap, ElementPoolService.GraphicsDevice.Viewport.Bounds));

        IsVisible = true;
    }

    public void Hide() => IsVisible = false;

    /// <summary>
    /// True pins width to MaximumSize.X, only height auto-grows -- HotbarController's Armed
    /// Hotkey Summary popup sets this, since it's pinned to HotbarContent.SummaryWidth. False
    /// (the default) lets width shrink to content like a normal WrapContent TextWindow, bounded
    /// by MaximumSize. A settable property, not a subclass override -- every Tooltip use so far
    /// (this one included) is a plain instance driven externally by whatever owns its hover
    /// state (see ShowNear's own doc comment); there's nothing else about the Armed Hotkey
    /// Summary popup that needs a dedicated type.
    /// </summary>
    public bool UseFixedWidth { get; set; }

    protected override void RecalculateWrapContentSize()
    {
        base.RecalculateWrapContentSize();

        if (!UseFixedWidth)
        {
            return;
        }

        // Re-pins width to MaximumSize.X after the base class's shrink-to-widest-line pass above.
        var fixedContentWidth = _geometry.MaximumSize.X - BorderInsetDoubled.X;
        _contentState.Size.X = fixedContentWidth;
        _contentState.BackgroundSize.X = fixedContentWidth;
        _geometry.CurrentSize.X = _geometry.MaximumSize.X;

        if (_headerState.ShowHeader)
        {
            _headerState.Size = new Vector2(fixedContentWidth, _headerState.Size.Y);
        }
    }
}
