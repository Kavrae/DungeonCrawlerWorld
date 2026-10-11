namespace Game.Sprites;

/// <summary>How a manifest cell is turned and flipped when drawn: flips first, then Rotation clockwise about the centre -- the order SpriteBatch applies SpriteEffects and rotation in.</summary>
/// <param name="Rotation">0, 90, 180 or 270 degrees clockwise.</param>
public readonly record struct SpriteCellTransform(int Rotation, bool FlipHorizontal, bool FlipVertical)
{
    public static SpriteCellTransform None => default;

    public bool IsNone => Rotation == 0 && !FlipHorizontal && !FlipVertical;

    public bool IsQuarterTurn => Rotation is 90 or 270;

    public static SpriteCellTransform Of(SpriteManifestCell cell) => new(cell.Rotation, cell.FlipHorizontal, cell.FlipVertical);
}

/// <summary>A manifest cell resolved for drawing: its pixels, how it is placed on its footprint (null stretches it to fill), how it is turned and flipped, and its tint (null for none).</summary>
public readonly record struct ManifestSprite(Modules.Core.Components.SpriteComponent Sprite, SpritePlacement? Placement, SpriteCellTransform Transform, Microsoft.Xna.Framework.Color? Tint = null);
