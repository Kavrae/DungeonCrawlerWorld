using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;

namespace UIEngine.UI;

/// <summary>A plain icon: a sprite by name, else a coloured glyph, drawn through SpriteOrGlyphRenderer to fill its content area.</summary>
/// <remarks>
/// No badge, glow or border of its own: a details row's item icon, a browser row's icon and symbols, a button's picture.
/// The glyph's font is a fraction of the icon's height (FontChrome.IconGlyphFontFraction) unless one is given. Inside a
/// disabled pressable control, sprite and glyph are both greyed.
/// </remarks>
public sealed class IconElement(ElementServices services, SpriteSheetService spriteSheetService) : Element(services)
{
    private SpriteView? _sprite;
    private string _glyph = string.Empty;
    private Color _glyphColor;
    private FontStashTextMeasurer _glyphFontMeasurer = null!;

    /// <summary>The glyph shown when the sprite isn't in the manifest.</summary>
    public string Glyph => _glyph;

    public Color GlyphColor => _glyphColor;

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        CanUserFocus = false;
    }

    /// <summary>Shows spriteName's sprite when the manifest has it, else glyph in glyphColor, sized for an icon iconSize across.</summary>
    public void Configure(string? spriteName, string glyph, Color glyphColor, Vector2 iconSize) =>
        Configure(spriteName, glyph, glyphColor, FontService.GetFont((int)(iconSize.Y * FontChrome.IconGlyphFontFraction)));

    /// <summary>Shows spriteName's sprite when the manifest has it, else glyph in glyphColor drawn in glyphFont.</summary>
    public void Configure(string? spriteName, string glyph, Color glyphColor, SpriteFontBase glyphFont)
    {
        _sprite = spriteSheetService.Sprites.FirstOrNull(spriteName);
        _glyph = glyph;
        _glyphColor = glyphColor;
        _glyphFontMeasurer = FontService.GetTextMeasurer(glyphFont);
    }

    public override void DrawContent(GameTime gameTime)
    {
        var isDisabled = PressableElement.IsHeldByDisabledControl(this);
        SpriteOrGlyphRenderer.Draw(Render.Batch, spriteSheetService, _sprite, _glyphFontMeasurer, _glyph, isDisabled ? Color.Gray : _glyphColor, Vector2.Zero, ContentSize, isDisabled ? Color.Gray : Color.White);
    }
}
