using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>
/// Draws a single glyph string at a position, and computes where to draw it so it's centered
/// within an arbitrary pixel footprint. Decoupled from Map/World/ComponentManager -- callers
/// (e.g. MapWindow) resolve which entity/font/position/color to use and pass those in as
/// plain values.
/// </summary>
public sealed class LabelRenderer
{
    // Keyed by (font, glyph) rather than measured fresh every call -- TextBounds is a pure
    // function of the font and exact string, and the same (font, glyph) pairs repeat every
    // frame for every entity of a given race/terrain type across the visible map (thousands
    // of draws/frame at typical zoom), so this turns a per-draw font measurement into a
    // one-time cost per distinct glyph the game ever actually uses.
    private readonly Dictionary<(SpriteFontBase Font, string Glyph), Vector2> _inkCenterCache = [];

    /// <summary>
    /// outline defaults false -- plain UI text (buttons, tabs, toggles, context menus, ...) sits on
    /// its own window background and doesn't need it. Map-drawn glyphs (entities, terrain, layer
    /// badges) pass true so they stay legible against whatever color terrain tile is underneath.
    ///
    /// position is rounded to the nearest whole pixel before drawing -- the actual root cause of
    /// the "bottom of the text is cut off" bug chased across several call sites (Tooltip, ability
    /// score rows, the trade window's own header labels): every render pass in this codebase uses
    /// SamplerState.PointClamp (see ElementPoolService.ResetRenderState), and point/nearest-neighbor
    /// sampling of a font atlas texture crops or shifts a glyph by roughly a texel whenever it's
    /// drawn at a fractional pixel position -- exactly what GetLeftAlignedPosition/
    /// GetRightAlignedPosition's own `/ 2f` centering math produces the moment the footprint's
    /// height-minus-LineHeight difference is odd. This is why routing a window through
    /// RequiresContentViewport's own pushed Viewport (Vector2.Zero + an integer LinePadding
    /// constant -- always an exact whole pixel) "fixed" it before: not the viewport/scissor
    /// mechanism itself, but the accidental side effect of drawing at an integer position instead
    /// of Element.ContentAbsolutePosition's own accumulated float sum, which is essentially never
    /// guaranteed to land on a whole pixel. Rounding once, here, fixes every consumer of this
    /// method at the source instead of requiring each one to separately discover the same
    /// workaround (CanUserScrollVertical = true) -- see TextWindow.DrawContent for the other half
    /// of this fix, the one direct SpriteBatch.DrawString call in the UI layer that doesn't route
    /// through this method.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, SpriteFontBase font, string glyph, Vector2 position, Color color, bool outline = false)
    {
        var roundedPosition = new Vector2(MathF.Round(position.X), MathF.Round(position.Y));

        if (outline)
        {
            ContrastTextRenderer.Draw(spriteBatch, font, glyph, roundedPosition, fillColor: color);
        }
        else
        {
            spriteBatch.DrawString(font, glyph, roundedPosition, color);
        }
    }

    /// <summary>
    /// Where glyph must be drawn so its actual rendered ink -- not the font's generic line
    /// box, which MeasureString returns and sits differently anchored than most glyphs' real
    /// ink (this is what previously made glyphs render too low: MeasureString("g") is a
    /// ~29px-tall line box, but the visible ink only occupies roughly its bottom two-thirds)
    /// -- centers within a footprintSize box whose top-left corner is footprintTopLeft. E.g. a
    /// 3x3 entity's footprint is 3 tiles wide/tall, not 1, so a "huge" font glyph centers
    /// across all three rather than sitting in the corner tile.
    /// </summary>
    public Vector2 GetCenteredPosition(SpriteFontBase font, string glyph, Vector2 footprintTopLeft, Vector2 footprintSize)
    {
        var footprintCenter = footprintTopLeft + footprintSize / 2f;
        return footprintCenter - GetInkCenterAtOrigin(font, glyph);
    }

    public void DrawCentered(SpriteBatch spriteBatch, SpriteFontBase font, string glyph, Vector2 footprintTopLeft, Vector2 footprintSize, Color color, bool outline = false) =>
        Draw(spriteBatch, font, glyph, GetCenteredPosition(font, glyph, footprintTopLeft, footprintSize), color, outline);

    /// <summary>
    /// Where text must be drawn so it's flush against footprintSize's right edge and vertically
    /// centered within it -- horizontally, a plain MeasureString-based line-box position, not
    /// GetCenteredPosition's ink-bound centering (right-aligned text like "Source : +2" needs its
    /// whole line box flush right so a column of these lines up cleanly, not each string's
    /// individual ink). Vertically, centers against font.LineHeight, not MeasureString(text).Y --
    /// the same "generic line box sits well below where the ink actually renders" fix
    /// GetCenteredPosition's own doc comment describes (e.g. "g" at font size 24 measures a
    /// ~29px-tall box but its ink only occupies roughly Y=[10,29] within it); using the box's own
    /// height here centered every row's text too low, bleeding descenders past the footprint's own
    /// bottom edge (confirmed live: AbilityScoreModifierRow's modifier text, ShopItemStackCell's
    /// name/price lines, Tooltip's status line -- every DrawLeftAligned/DrawRightAligned consumer).
    /// LineHeight is a per-font constant (not per-string), which also keeps a column of rows
    /// vertically consistent regardless of which ones happen to have descenders -- same property
    /// TextDivider's own DrawContent already uses for exactly this reason.
    /// </summary>
    public Vector2 GetRightAlignedPosition(SpriteFontBase font, string text, Vector2 footprintTopLeft, Vector2 footprintSize)
    {
        var textWidth = font.MeasureString(text).X;
        return footprintTopLeft + new Vector2(footprintSize.X - textWidth, (footprintSize.Y - font.LineHeight) / 2f);
    }

    public void DrawRightAligned(SpriteBatch spriteBatch, SpriteFontBase font, string text, Vector2 footprintTopLeft, Vector2 footprintSize, Color color) =>
        Draw(spriteBatch, font, text, GetRightAlignedPosition(font, text, footprintTopLeft, footprintSize), color);

    /// <summary>Where text must be drawn so it's flush against footprintSize's left edge and vertically centered within it -- the left-aligned counterpart to GetRightAlignedPosition (see its own doc comment for the LineHeight-vs-MeasureString fix this shares), for a row that needs both (e.g. a context-menu option's label on the left, its hotkey on the right).</summary>
    public Vector2 GetLeftAlignedPosition(SpriteFontBase font, string text, Vector2 footprintTopLeft, Vector2 footprintSize) =>
        footprintTopLeft + new Vector2(0, (footprintSize.Y - font.LineHeight) / 2f);

    public void DrawLeftAligned(SpriteBatch spriteBatch, SpriteFontBase font, string text, Vector2 footprintTopLeft, Vector2 footprintSize, Color color) =>
        Draw(spriteBatch, font, text, GetLeftAlignedPosition(font, text, footprintTopLeft, footprintSize), color);

    /// <summary>The center of glyph's tight ink bounding box if drawn at (0,0) -- TextBounds translates linearly with position, so this alone is enough to center at any footprint.</summary>
    private Vector2 GetInkCenterAtOrigin(SpriteFontBase font, string glyph)
    {
        var key = (font, glyph);
        if (_inkCenterCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var bounds = font.TextBounds(glyph, Vector2.Zero, null, 0f, 0f);
        var inkCenter = new Vector2((bounds.X + bounds.X2) / 2f, (bounds.Y + bounds.Y2) / 2f);
        _inkCenterCache[key] = inkCenter;
        return inkCenter;
    }
}