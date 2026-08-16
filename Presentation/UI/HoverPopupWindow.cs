using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;

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
/// availableSize is read from, then written straight back to, MaximumSize -- a no-op). The
/// tradeoff is the caller owns raising it above whatever else is on screen -- see
/// containerElements.
/// </summary>
public class HoverPopupWindow(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer, List<Element>? containerElements = null)
    : TextWindow(fontService, elementPoolService, glyphRenderer)
{
    /// <summary>Repositions and shows this popup next to target -- a title bar only if title is supplied (toggled dynamically since one shared instance may be used both with and without a title across calls, e.g. AbilityScoreWindow's score-description vs. modifier-source popups). Also re-appends this window to the end of containerElements, if one was supplied at construction, so it draws on top of everything else already in that tier (e.g. AbilityScoreWindow, added to the same DynamicHUD list earlier) -- a no-op for a popup like ArmedHotkeySummaryWindow that doesn't need this (Hotbar's own StaticHUD tier already draws before DynamicHUD). Call every frame the same thing should stay hovered -- cheap no-op churn, same as ArmedHotkeySummaryWindow's own prior per-frame Update.</summary>
    public void ShowNear(Rectangle target, PopupAnchor anchor, Vector2 gap, string body, string? title = null)
    {
        _headerState.ShowHeader = title is not null;
        TitleText = title ?? string.Empty;

        UpdateText(body); // Resizes CurrentSize to the new content first -- PopupPositioning below needs the real, post-resize size.
        SetRelativePosition(PopupPositioning.GetPosition(target, CurrentSize, anchor, gap)); // Always top-level, so RelativePosition == absolute screen position.

        if (containerElements is not null)
        {
            containerElements.Remove(this);
            containerElements.Add(this);
        }

        IsVisible = true;
    }

    public void Hide() => IsVisible = false;

    /// <summary>True for a popup with a fixed width and only auto-growing height -- ArmedHotkeySummaryWindow's own requirement, since it's pinned to HotbarContent.SummaryWidth. False (the default) lets width shrink to content like a normal WrapContent TextWindow, bounded by MaximumSize.</summary>
    protected virtual bool UseFixedWidth => false;

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
        _geometry.CurrentSize.X = _geometry.MaximumSize.X;

        if (_headerState.ShowHeader)
        {
            _headerState.Size = new Vector2(fixedContentWidth, _headerState.Size.Y);
        }
    }
}
