namespace SpriteFormat;

/// <summary>A rectangle of a spritesheet in source pixels: where a cell's picture is.</summary>
public readonly record struct SpriteSourceRectangle(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}
