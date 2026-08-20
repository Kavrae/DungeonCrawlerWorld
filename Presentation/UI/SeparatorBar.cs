using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI;

/// <summary>
/// A thin horizontal divider, drawn centered at 75% of its own content width in a caller-supplied
/// color -- e.g. AbilityScoreWindow's group separators between Base/Additive/Multiplicative
/// modifier lines (1px tall there, via the caller sizing this element's own Layout.Size.Y to 1).
/// Sized to its full footprint like any sibling row in a Vertical tile chain (see Element.
/// RetileChildrenFrom, which carries a child's own X position forward onto the next sibling) --
/// the narrower bar is purely a DrawContent detail; the element's own bounds stay full width/
/// height to avoid disturbing that chain.
/// </summary>
public sealed class SeparatorBar(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer)
    : Element(fontService, elementPoolService, glyphRenderer)
{
    private const float WidthFraction = 0.75f;

    private Color _color;

    public void Configure(Color color) => _color = color;

    public override void DrawContent(GameTime gameTime)
    {
        var barWidth = ContentSize.X * WidthFraction;
        var barPosition = ContentAbsolutePosition + new Vector2((ContentSize.X - barWidth) / 2f, 0);

        ElementPoolService.SpriteBatch.Draw(ElementPoolService.UnitRectangle, new Rectangle((int)barPosition.X, (int)barPosition.Y, (int)barWidth, (int)ContentSize.Y), _color);
    }
}
