using System.Globalization;
using Microsoft.Xna.Framework;

namespace Game.Sprites;

/// <summary>A manifest cell's tint: a colour multiplied into its pixels, written "#RRGGBB" or "#RRGGBBAA".</summary>
public static class SpriteTint
{
    public static bool TryParse(string text, out Color tint)
    {
        tint = Color.White;
        if (text.Length is not (7 or 9) || text[0] != '#' || !uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        tint = text.Length == 7
            ? new Color((byte)(value >> 16), (byte)(value >> 8), (byte)value, (byte)255)
            : new Color((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    /// <summary>The "#RRGGBB" text for tint, or "#RRGGBBAA" when it isn't fully opaque.</summary>
    public static string Format(Color tint) =>
        tint.A == 255 ? $"#{tint.R:X2}{tint.G:X2}{tint.B:X2}" : $"#{tint.R:X2}{tint.G:X2}{tint.B:X2}{tint.A:X2}";

    /// <summary>Each channel of first multiplied by the same channel of second, as SpriteBatch does a sprite's pixels by its colour.</summary>
    public static Color Multiply(Color first, Color second) =>
        new((byte)(first.R * second.R / 255), (byte)(first.G * second.G / 255), (byte)(first.B * second.B / 255), (byte)(first.A * second.A / 255));
}
