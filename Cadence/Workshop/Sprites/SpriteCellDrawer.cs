using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SpriteFormat;
using UIEngine.Rendering;

namespace Workshop.Sprites;

/// <summary>Draws a manifest cell -- its sheet's pixels, turned, flipped and tinted as the cell says -- the way the map draws a sprite (SpriteRenderer), so a preview through the sprite effect looks as the game does.</summary>
/// <param name="sheetExists">Whether a sheet path is a file in the spritesheets folder; a cell on a missing sheet draws nothing (the Error List names it).</param>
public sealed class SpriteCellDrawer(SpriteSheetService spriteSheetService, Func<string, bool> sheetExists)
{
    private readonly Dictionary<string, SpriteSheet?> _sheetsByPath = new(StringComparer.Ordinal);

    /// <summary>The texture of the sheet at path, or null when there is no such file.</summary>
    public Texture2D? TextureOf(string sheetPath)
    {
        if (!_sheetsByPath.TryGetValue(sheetPath, out var sheet))
        {
            sheet = sheetExists(sheetPath) ? new SpriteSheet(sheetPath) : null;
            _sheetsByPath.Add(sheetPath, sheet);
        }

        return sheet is null ? null : spriteSheetService.GetTexture(sheet);
    }

    /// <summary>cell stretched over the rectangle topLeft and size describe, faded by alphaMultiplier.</summary>
    public void Draw<TTarget>(TTarget target, SpriteManifestCell cell, Vector2 topLeft, Vector2 size, float alphaMultiplier = 1f)
        where TTarget : ISpriteDrawTarget
    {
        if (TextureOf(cell.SheetPath) is not { } texture)
        {
            return;
        }

        var source = cell.SourceRectangle;
        var tint = cell.TintColor is { } cellTint ? new Color(cellTint.R, cellTint.G, cellTint.B, cellTint.A) : Color.White;
        SpriteRenderer.Draw(target, texture, new Rectangle(source.X, source.Y, source.Width, source.Height), topLeft, size, tint * alphaMultiplier, SpriteCellTransform.Of(cell));
    }

    /// <summary>A flat rectangle's outline, thickness pixels wide, inside it.</summary>
    public static void DrawOutline<TTarget>(TTarget target, Texture2D unitRectangle, Rectangle rectangle, Color color, int thickness)
        where TTarget : ISpriteDrawTarget
    {
        target.Draw(unitRectangle, new Rectangle(rectangle.X, rectangle.Y, rectangle.Width, thickness), null, color, SpriteDrawStyle.Plain);
        target.Draw(unitRectangle, new Rectangle(rectangle.X, rectangle.Bottom - thickness, rectangle.Width, thickness), null, color, SpriteDrawStyle.Plain);
        target.Draw(unitRectangle, new Rectangle(rectangle.X, rectangle.Y, thickness, rectangle.Height), null, color, SpriteDrawStyle.Plain);
        target.Draw(unitRectangle, new Rectangle(rectangle.Right - thickness, rectangle.Y, thickness, rectangle.Height), null, color, SpriteDrawStyle.Plain);
    }
}
