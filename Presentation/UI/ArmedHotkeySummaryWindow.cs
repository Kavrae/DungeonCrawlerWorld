using Game.Modules.Abilities;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Content;

namespace Presentation.UI;

/// <summary>
/// Shows the name/summary (see AbilityDefinition/ItemDefinition's Summary vs Description doc
/// comments -- this window uses Summary, the concise one) of whichever hotbar slot is currently
/// armed, click-previewed, or hovered (see HotbarController, the only writer of MapViewState.
/// ArmedSlot/PreviewSlot/HoverSlot relevant here) -- a single persistent, pooled-style TextWindow
/// toggled via IsVisible rather than created/closed per notification the way NotificationCenter's
/// own popups are, since this shows/hides far more often. Width is fixed (HotbarContent.
/// SummaryWidth, set via this window's MaximumSize.X at construction) with only height
/// auto-growing with content -- see the RecalculateWrapContentSize override below.
/// </summary>
public sealed class ArmedHotkeySummaryWindow(
    FontService fontService,
    ElementPoolService elementPoolService,
    GlyphRenderer glyphRenderer,
    MapViewState mapViewState,
    HotbarContent hotbarContent)
    : TextWindow(fontService, elementPoolService, glyphRenderer)
{
    /// <summary>Bottom edge sits exactly this far above the summarized slot's top edge.</summary>
    private const float BelowHotbarGap = 1f;

    private HotkeySlot? _displayedSlot;

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        var slotToShow = mapViewState.HoverSlot ?? mapViewState.ArmedSlot ?? mapViewState.PreviewSlot;

        if (slotToShow == _displayedSlot)
        {
            return;
        }

        _displayedSlot = slotToShow;

        if (slotToShow is not { } slot || !hotbarContent.TryGetSlotSummary(slot, out var title, out var summary))
        {
            IsVisible = false;
            return;
        }

        TitleText = title;
        UpdateText(summary); // Re-triggers RecalculateWrapContentSize (fixed width, auto height -- see override below) + Arrange().

        var slotBounds = hotbarContent.GetSlotBounds(slot);
        SetRelativePosition(new Vector2(
            slotBounds.Center.X - CurrentSize.X / 2f,
            slotBounds.Top - CurrentSize.Y - BelowHotbarGap));

        IsVisible = true;
    }

    protected override void RecalculateWrapContentSize()
    {
        base.RecalculateWrapContentSize();

        // Base TextWindow always shrinks content width to the widest wrapped line (see
        // TextWindow.RecalculateWrapContentSize). This window's width is FIXED (set via
        // MaximumSize.X at construction, see HotbarController.Initialize) with only height
        // auto-growing, so pin it back after the base class's wrap/height/scroll-bounds math has
        // already run against that same fixed maximum width.
        var fixedContentWidth = _geometry.MaximumSize.X - BorderInsetDoubled.X;
        _contentState.Size.X = fixedContentWidth;
        _geometry.CurrentSize.X = _geometry.MaximumSize.X;

        if (_headerState.ShowHeader)
        {
            _headerState.Size = new Vector2(fixedContentWidth, _headerState.Size.Y);
        }
    }
}
