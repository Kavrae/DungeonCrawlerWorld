using FontStashSharp;
using Game.Blueprints;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.Content;

/// <summary>
/// One square in the inventory grid: an item stack's sprite-else-glyph icon, plus its quantity
/// in the bottom-right corner when greater than 1. A plain Element (not Window) -- no title/
/// chrome needed, same reasoning Folder/Button use. IsDisabled gray-tints the icon, mirroring
/// Folder's own disabled tint and MapWindow's dead-entity tint (all three now share the same
/// SpriteOrGlyphRenderer draw primitive). ItemDefinitionId is exposed publicly so
/// UiInputController can read it directly (no Element-level drag hook needed -- see its own
/// content-drag state machine) when a press starts a drag from this cell toward a hotbar slot.
///
/// Once per-slot item divergence exists (see InventoryGridContent's own GroupDivergedStacks),
/// this cell shows one of three things -- terms this codebase uses consistently for all three:
/// a **Base item stack** (StackInstanceId set, IsDivergent false -- an ordinary, undiverged
/// stack), a **Diverging item stack** shown on its own (StackInstanceId set, IsDivergent true --
/// its own Override differs from the catalog original, e.g. a wand's own remaining charges), or
/// a **Merged Stack**: a display-only cell standing in for two or more stacks (any mix of Base/
/// Diverging) that share one ItemDefinitionId (StackInstanceId null -- there is no single stack a
/// click/drag against this cell could mean; MergedStackBadgeVisible true whenever at least one
/// member is Diverging). Clicking a Merged Stack's badge expands it into its own **Expansion Stacks** --
/// the same underlying Base/Diverging cells it was standing in for, now shown individually (see
/// InventoryGridContent.OnCellClicked/RebuildCells). CanBindToHotbar is false only for the Merged
/// Stack case -- a Diverging item stack (whether standalone or currently shown as one of a Merged
/// Stack's Expansion Stacks) is exactly as bindable as a Base one (see that property's own doc
/// comment for why). A Merged Stack can still be dragged (see UiInputController's own content-drag
/// path) -- just never dropped onto a hotbar slot -- so a future manual-sort gesture has something
/// to build on; it's the one case that gets the disabled-cursor treatment while hovering a hotbar
/// slot mid-drag.
/// </summary>
public sealed class InventoryItemStackCell(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer, SpriteSheetService spriteSheetService, SpriteRenderer spriteRenderer)
    : Element(fontService, elementPoolService, glyphRenderer)
{
    private const float IconGlyphFontFraction = 0.6f;
    private const float QuantityFontFraction = 0.5f;
    private const float BadgeFontFraction = 0.45f;
    private static readonly Color GroupBorderColor = Color.Black;
    private const float GroupBorderThickness = 2f;

    private string? _spriteName;
    private string _glyph = string.Empty;
    private Color _glyphColor;
    private int _quantity;
    private string? _chargeText;
    private bool _isDisabled;
    private bool _groupBorderTop;
    private bool _groupBorderBottom;
    private bool _groupBorderLeft;
    private bool _groupBorderRight;
    private SpriteFontBase _iconGlyphFont = null!;
    private SpriteFontBase _quantityFont = null!;
    private SpriteFontBase _badgeFont = null!;

    public Guid ItemDefinitionId { get; private set; }

    /// <summary>The exact stack this cell represents -- what UiInputController's content-drag path reads to bind a hotbar slot to one specific physical stack, not just "some stack of this item id" (see ItemHotkeyBindingComponent's own doc comment). Null for a merged group cell (see this class's own doc comment) -- there is no single stack to bind.</summary>
    public Guid? StackInstanceId { get; private set; }

    /// <summary>True when this cell shows one single divergent stack on its own (not merged into a group) -- see this class's own doc comment.</summary>
    public bool IsDivergent { get; private set; }

    /// <summary>True when this cell is a merged group containing at least one divergent member -- drives the "+" Merged Stack badge, the signal a click should expand it (see InventoryGridContent.RebuildCells).</summary>
    public bool MergedStackBadgeVisible { get; private set; }

    /// <summary>
    /// False only for a merged group cell (StackInstanceId null -- there is no single stack a
    /// drop onto a hotbar slot could mean). A single divergent stack -- whether it never needed
    /// merging in the first place, or is currently shown as one of a Merged Stack's Expansion
    /// Stacks after clicking to expand -- is exactly as bindable as a base stack: it already
    /// resolves to one exact physical StackInstanceId, the same identity ConsumableActivationSystem's
    /// own PeelWandCharge repoints a binding to automatically once a wand fires. Divergence itself
    /// was never the thing worth blocking -- ambiguity (which physical stack a merged cell's drag
    /// would even mean) is.
    /// </summary>
    public bool CanBindToHotbar => StackInstanceId is not null;

    /// <summary>Drives a translucent highlight overlay -- see InventoryGridContent's own hover polling. Mirrors AbilityScoreColumnHeader.IsHovered.</summary>
    public bool IsHovered { get; set; }

    /// <summary>
    /// cellSize is the caller's known fixed cell size (see InventoryGridContent), not ContentSize
    /// -- Configure runs immediately after CreateElement, before this cell's own layout has
    /// necessarily settled. Unconditionally clears any group-border edges left over from a
    /// previous Configure -- ElementPoolService reuses cell instances, so a cell that was
    /// previously an expanded-group member must not still be drawing that border once it's
    /// reconfigured for an unrelated stack (confirmed bug: the border "leaked" onto other items
    /// this way). SetGroupBorderEdges, called separately afterward, is the only thing that turns
    /// any of them back on for this Configure's cell.
    /// </summary>
    public void Configure(Guid itemDefinitionId, Guid? stackInstanceId, string? spriteName, string glyph, Color glyphColor, int quantity, string? chargeText, bool isDisabled, bool isDivergent, bool mergedStackBadgeVisible, Vector2 cellSize)
    {
        ItemDefinitionId = itemDefinitionId;
        StackInstanceId = stackInstanceId;
        IsDivergent = isDivergent;
        MergedStackBadgeVisible = mergedStackBadgeVisible;
        _spriteName = spriteName;
        _glyph = glyph;
        _glyphColor = glyphColor;
        _quantity = quantity;
        _chargeText = chargeText;
        _isDisabled = isDisabled;
        _groupBorderTop = false;
        _groupBorderBottom = false;
        _groupBorderLeft = false;
        _groupBorderRight = false;
        _iconGlyphFont = fontService.GetFont((int)(cellSize.Y * IconGlyphFontFraction));
        _quantityFont = fontService.GetFont((int)(cellSize.Y * QuantityFontFraction));
        _badgeFont = fontService.GetFont((int)(cellSize.Y * BadgeFontFraction));
    }

    /// <summary>
    /// Which of this cell's four edges sit on the outer perimeter of the currently-expanded
    /// group it belongs to -- an edge draws the group border only when true (its row-major
    /// neighbor on that side isn't also a member of the group, or there is no neighbor at all).
    /// Only ever called for an expanded group's own member cells (see InventoryGridContent.
    /// RebuildCells) -- every other cell stays at Configure's own all-false default, so this
    /// border never shows anywhere else.
    /// </summary>
    public void SetGroupBorderEdges(bool top, bool bottom, bool left, bool right)
    {
        _groupBorderTop = top;
        _groupBorderBottom = bottom;
        _groupBorderLeft = left;
        _groupBorderRight = right;
    }

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = ElementPoolService.SpriteBatch;
        var unitRectangle = ElementPoolService.UnitRectangle;

        if (IsHovered)
        {
            spriteBatch.Draw(unitRectangle, new Rectangle((int)ContentAbsolutePosition.X, (int)ContentAbsolutePosition.Y, (int)ContentSize.X, (int)ContentSize.Y), WindowPalette.HighlightColor);
        }

        SpriteComponent? sprite = _spriteName is not null && SpriteManifest.TryGet(_spriteName, out var spriteComponent) ? spriteComponent : null;
        var spriteTint = _isDisabled ? Color.Gray : Color.White;
        var glyphColor = _isDisabled ? Color.Gray : _glyphColor;

        SpriteOrGlyphRenderer.Draw(spriteBatch, spriteSheetService, spriteRenderer, GlyphRenderer, sprite, _iconGlyphFont, _glyph, glyphColor, ContentAbsolutePosition, ContentSize, spriteTint);

        ItemIconRenderer.DrawQuantityBadge(spriteBatch, _quantityFont, _quantity, _chargeText, ContentAbsolutePosition, ContentSize);

        if (MergedStackBadgeVisible)
        {
            var badgeSize = _badgeFont.MeasureString("+");
            var badgePosition = new Vector2(ContentAbsolutePosition.X + ContentSize.X - badgeSize.X, ContentAbsolutePosition.Y);
            ContrastTextRenderer.Draw(spriteBatch, _badgeFont, "+", badgePosition);
        }

        DrawGroupBorder(spriteBatch, unitRectangle);
    }

    /// <summary>
    /// Replaces this cell's own normal border (see InventoryGridContent.RebuildCells, which
    /// passes ShowBorder: false for an expanded group's member cells) rather than drawing
    /// alongside it -- a solid black line only on the group's outer perimeter edges, so two
    /// adjacent member cells share a clean, unbroken join with no border between them at all.
    /// </summary>
    private void DrawGroupBorder(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (!_groupBorderTop && !_groupBorderBottom && !_groupBorderLeft && !_groupBorderRight)
        {
            return;
        }

        var bounds = new Rectangle((int)ContentAbsolutePosition.X, (int)ContentAbsolutePosition.Y, (int)ContentSize.X, (int)ContentSize.Y);
        var thickness = BorderThickness.Uniform(new Vector2(GroupBorderThickness, GroupBorderThickness));
        var (top, bottom, left, right) = BorderThickness.GetEdgeRectangles(bounds, thickness);

        BorderRenderer.Draw(
            spriteBatch, unitRectangle, BorderStyle.Flat, GroupBorderColor,
            _groupBorderTop ? top : Rectangle.Empty,
            _groupBorderBottom ? bottom : Rectangle.Empty,
            _groupBorderLeft ? left : Rectangle.Empty,
            _groupBorderRight ? right : Rectangle.Empty);
    }
}
