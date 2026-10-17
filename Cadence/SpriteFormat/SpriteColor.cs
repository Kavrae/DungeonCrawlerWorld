namespace SpriteFormat;

/// <summary>A colour as the manifest stores one, channel by channel, 255 fully on.</summary>
public readonly record struct SpriteColor(byte R, byte G, byte B, byte A = 255)
{
    public static SpriteColor White => new(255, 255, 255, 255);
}
