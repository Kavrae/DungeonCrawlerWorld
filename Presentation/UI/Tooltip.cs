using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI;

/// <summary>
/// One extra line drawn below a Tooltip's wrapped body text -- LeftText flush left, RightText
/// flush right (mirrors Button's own LeftText/RightText row shape), both in Color. RightText empty
/// draws as a plain single-column line (e.g. "Shop will not buy"). MiddleText, when non-empty,
/// draws left-aligned in a third column between the two -- e.g. the band table's own stock range
/// (see InventoryGridContent.ComputeHoverRows) -- at a shared column start computed across every
/// row in the block that uses one, so the ranges line up regardless of each row's own LeftText
/// length. IsDivider true draws a thin horizontal rule in Color instead of any text (LeftText/
/// MiddleText/RightText ignored) -- see Divider below. GlowColor, when set, draws an inner-fade
/// glow (GlowRenderer.InteriorFade) around this row's own bounds in that color -- what marks the
/// shop's *current* band on the band table, instead of coloring that row's text.
/// </summary>
public readonly record struct TooltipRow(string LeftText, string RightText, Color Color, bool IsDivider = false, Color? GlowColor = null, string MiddleText = "")
{
    public static TooltipRow Divider(Color color) => new(string.Empty, string.Empty, color, IsDivider: true);
}

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
    /// <summary>Same dark fill as WindowPalette.PanelBackgroundColor (45,45,45), but a touch more opaque -- 90% instead of the shared 85% -- confirmed live that the shop band table's dividers, range column, and current-band glow read more clearly against a less translucent background. Applied only here, not by raising WindowPalette.PanelBackgroundColor itself, which every other window (HealthWindow, ItemDetailsWindow, ShopWindow, InventoryManagementWindow, etc.) also uses and shouldn't change.</summary>
    private static readonly Color BackgroundColor = new Color(45, 45, 45) * 0.90f;

    /// <summary>TextWindow's own default (WindowPalette.BodyTextColor, Black) reads fine against a light content background -- every Tooltip popup instead sits on this class's own BackgroundColor dark fill, where black text is illegible. No caller currently opts out with its own explicit TextOptions.TextColor, but this still only overrides the fallback, not an explicit choice.</summary>
    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        TextColor = options.Text?.TextColor ?? WindowPalette.TitleTextColor;
        SetContentColor(options.Content?.ContentColor ?? BackgroundColor);

        // DIAGNOSTIC/candidate fix, not yet confirmed: forces RequiresContentViewport true for
        // every Tooltip regardless of what ElementOptions.Chrome any given caller passed (none
        // currently set CanUserScrollVertical at all). NotificationCenter's own achievement popup
        // -- a plain TextWindow like this one, same base.DrawContent, confirmed rendering cleanly
        // live -- sets CanUserScrollVertical true, which is the one concrete structural difference
        // found between it and Tooltip's own, still-broken multi-line body text: with
        // RequiresContentViewport true, Element.Draw wraps DrawContent in a dedicated Viewport/
        // CameraTransform pass and TextWindow.DrawContent's own origin switches from
        // ContentAbsolutePosition (absolute screen coordinates) to Vector2.Zero (local to that
        // viewport) -- without it, Tooltip draws directly in screen space instead. MaximumSize is
        // already generous enough (10000f tall for every real caller) that content essentially
        // never actually needs to scroll, so this shouldn't introduce a visible scrollbar -- it's
        // only here for whatever rendering-path difference RequiresContentViewport itself causes.
        CanUserScrollVertical = true;
    }

    /// <summary>
    /// Repositions and shows this popup next to target -- a titleText bar only if titleText is
    /// supplied (toggled dynamically since one shared instance may be used both with and without a
    /// titleText across calls, e.g. AbilityScoreWindow's score-description vs. modifier-source
    /// popups). Call every frame the same thing should stay hovered -- cheap no-op churn, the same
    /// idiom every current caller (AbilityScoreWindow, InventoryGridContent, HotbarController)
    /// already uses for its own hover-driven popup. rows is the same "only if supplied" shape as
    /// titleText, one level further -- InventoryGridContent's shop-mode hover is the one caller
    /// that passes it (see SetRows' own doc comment); every other caller omits it, which clears any
    /// rows this same pooled instance might still be carrying from an earlier ShowNear call.
    /// </summary>
    public void ShowNear(Rectangle target, PopupAnchor anchor, Vector2 gap, string bodyText, string? titleText = null, IReadOnlyList<TooltipRow>? rows = null)
    {
        _headerState.ShowHeader = titleText is not null;
        TitleText = titleText ?? string.Empty;
        SetRows(rows);

        UpdateText(bodyText);
        SetRelativePosition(PopupPositioning.GetPositionWithinBounds(target, CurrentSize, anchor, gap, ElementPoolService.GraphicsDevice.Viewport.Bounds));

        IsVisible = true;
    }

    public void Hide() => IsVisible = false;

    private IReadOnlyList<TooltipRow>? _rows;

    /// <summary>
    /// Extra lines drawn below the wrapped body text, one per TooltipRow, each in its own color --
    /// e.g. shop mode's stock-band table and per-trade bracket receipt (see
    /// InventoryGridContent.UpdateHover). Drawn directly via LabelRenderer in DrawContent below, not
    /// through DisplayText/TextColor's own single-color pipeline (TextWindow has no notion of a
    /// second color), the same "custom draw alongside the inherited one" shape ShopItemStackCell's
    /// own price-line color already uses. null (or empty) clears them. Normally set via ShowNear,
    /// not called directly -- exposed separately so RecalculateWrapContentSize/DrawContent below
    /// have one clear source of truth to read.
    /// </summary>
    public void SetRows(IReadOnlyList<TooltipRow>? rows) => _rows = rows is { Count: > 0 } ? rows : null;

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

    /// <summary>Margin around the whole row block on all 4 sides (independent of LinePadding, which is TextWindow's own body-text margin) -- confirmed live that the row block needed its own breathing room from the tooltip's edges, not just from the body text above it.</summary>
    private const float RowPadding = 2f;

    /// <summary>
    /// Flat extra box width whenever rows are present -- confirmed live that RowPadding's own
    /// left+right inset (4px) alone still left a shop-mode tooltip too narrow for its price column,
    /// so this adds headroom beyond just the padding itself. Not applied when rows are absent (a
    /// plain description-only tooltip doesn't need it).
    /// </summary>
    private const float ExtraWidthForRows = 8f;

    /// <summary>Gap between the middle column's shared start (the widest LeftText among rows that use one, see MiddleText's own doc comment) and that longest LeftText itself, so the range text doesn't sit flush against the band name.</summary>
    private const float MiddleColumnGap = 6f;

    protected override void RecalculateWrapContentSize()
    {
        base.RecalculateWrapContentSize();

        // Extends the sizing by one more text line per row, a LinePadding gap above the whole
        // block (see DrawContent's matching offset), and RowPadding on top and bottom of the block
        // -- has to run before the UseFixedWidth block below since that block only ever touches X,
        // never Y. RowPadding's own top+bottom contribution here is exactly the "4px taller"
        // this needed -- ExtraWidthForRows carries the analogous width-side growth instead of
        // adding a second flat height constant here.
        if (_rows is { } rows)
        {
            var rowsHeight = LinePadding + RowPadding * 2 + ContentFont.LineHeight * rows.Count;
            _contentState.Size.Y += rowsHeight;
            _contentState.BackgroundSize.Y += rowsHeight;
            _geometry.CurrentSize.Y += rowsHeight;

            _contentState.Size.X += ExtraWidthForRows;
            _contentState.BackgroundSize.X += ExtraWidthForRows;
            _geometry.CurrentSize.X += ExtraWidthForRows;
        }

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

    /// <summary>
    /// Draws the inherited body text unchanged, then -- only if SetRows gave it something -- one
    /// more line per row directly beneath it, each RowPadding-inset from the block's own left/right
    /// edges: LeftText/RightText flush to either inset edge for a plain row (RightText empty draws
    /// as a single-column line), a thin horizontal rule for IsDivider, and an inner-fade glow around
    /// the row's own bounds when GlowColor is set. See _rows' own doc comment for why this bypasses
    /// TextWindow's single-TextColor DrawContent pipeline instead of extending it.
    /// </summary>
    public override void DrawContent(GameTime gameTime)
    {
        base.DrawContent(gameTime);

        if (_rows is not { } rows)
        {
            return;
        }

        // Raw content-area origin (no LinePadding baked in yet, unlike the base class's own
        // "origin" for body text) -- the row block applies its own (LinePadding + RowPadding)
        // inset on the left, distinct from the plain LinePadding-only inset the body text uses.
        var contentTopLeft = RequiresContentViewport ? Vector2.Zero : ContentAbsolutePosition;
        var blockLeft = LinePadding + RowPadding;
        var blockTop = ContentFont.LineHeight * DisplayText.LineCount + LinePadding + RowPadding;
        var rowWidth = System.Math.Max(0f, _contentState.Size.X - (LinePadding + RowPadding) * 2);
        var rowSize = new Vector2(rowWidth, ContentFont.LineHeight);

        // RightText's own footprint, inset from the row's right edge by the interior glow's own
        // width -- flush against rowSize instead, RightText would sit directly under the current
        // band's fade rings (see GlowRenderer.FadeRingCount's own doc comment), unreadable against
        // its brightest, innermost ring. Applied to every row, not just the glowing one, so the
        // price column still lines up straight down the block.
        var priceRowSize = new Vector2(System.Math.Max(0f, rowSize.X - GlowRenderer.FadeRingCount), rowSize.Y);

        // Shared start for every row's MiddleText, so the range column lines up down the block
        // instead of trailing right after each row's own differently-sized LeftText.
        var middleColumnLeft = 0f;
        foreach (var row in rows)
        {
            if (row.IsDivider || string.IsNullOrEmpty(row.MiddleText))
            {
                continue;
            }

            middleColumnLeft = System.Math.Max(middleColumnLeft, ContentFont.MeasureString(row.LeftText).X);
        }

        middleColumnLeft += MiddleColumnGap;

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var rowPosition = contentTopLeft + new Vector2(blockLeft, blockTop + ContentFont.LineHeight * rowIndex);
            var rowBounds = new Rectangle((int)rowPosition.X, (int)rowPosition.Y, (int)rowSize.X, (int)rowSize.Y);

            if (row.IsDivider)
            {
                var lineRectangle = new Rectangle(rowBounds.X, rowBounds.Y + rowBounds.Height / 2, rowBounds.Width, 1);
                ElementPoolService.SpriteBatch.Draw(ElementPoolService.UnitRectangle, lineRectangle, row.Color);
                continue;
            }

            LabelRenderer.DrawLeftAligned(ElementPoolService.SpriteBatch, ContentFont, row.LeftText, rowPosition, rowSize, row.Color);
            if (!string.IsNullOrEmpty(row.MiddleText))
            {
                var middlePosition = rowPosition + new Vector2(middleColumnLeft, 0);
                LabelRenderer.DrawLeftAligned(ElementPoolService.SpriteBatch, ContentFont, row.MiddleText, middlePosition, rowSize, row.Color);
            }

            if (!string.IsNullOrEmpty(row.RightText))
            {
                LabelRenderer.DrawRightAligned(ElementPoolService.SpriteBatch, ContentFont, row.RightText, rowPosition, priceRowSize, row.Color);
            }

            if (row.GlowColor is { } glowColor)
            {
                GlowRenderer.Draw(ElementPoolService.SpriteBatch, ElementPoolService.UnitRectangle, rowBounds, glowColor, GlowMode.InteriorFade);
            }
        }
    }
}
