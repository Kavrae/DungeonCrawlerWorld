using Microsoft.Xna.Framework;

namespace Game.Modules.Core.Components;

/// <summary>
/// How an entity is displayed on the map when a texture-backed visual (rather than a
/// GlyphComponent text glyph) is available. MapWindow prefers this over GlyphComponent when
/// both are present -- see MapWindow.TryDrawEntityVisual.
/// </summary>
public struct SpriteComponent(string sheetPath, Rectangle sourceRectangle)
{
    /// <summary>Path to the spritesheet texture, relative to the spritesheets root directory.</summary>
    public string SheetPath { get; set; } = sheetPath;

    /// <summary>The pixel-space cell within SheetPath to draw.</summary>
    public Rectangle SourceRectangle { get; set; } = sourceRectangle;

    /// <summary>Builds a SourceRectangle from grid coordinates rather than raw pixel math -- most spritesheets in this game are laid out as a uniform grid of cellWidth x cellHeight cells.</summary>
    public static SpriteComponent FromCell(string sheetPath, int column, int row, int cellWidth = 16, int cellHeight = 16) =>
        new(sheetPath, new Rectangle(column * cellWidth, row * cellHeight, cellWidth, cellHeight));

    public override readonly string ToString() => $"Sprite : {SheetPath} {SourceRectangle}";
}
