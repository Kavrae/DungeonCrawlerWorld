using System.Globalization;

namespace SpriteManifestBuilder;

/// <summary>A cell's tint as the manifest writes it, "#RRGGBB" or "#RRGGBBAA". Mirrors Game/Sprites/SpriteTint.cs; keep the two in sync by hand.</summary>
public static class Tints
{
    public static bool TryParse(string text, out Color tint)
    {
        tint = Color.White;
        if (text.Length is not (7 or 9) || text[0] != '#' || !uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        tint = text.Length == 7
            ? Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : Color.FromArgb((byte)value, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8));
        return true;
    }

    /// <summary>"#RRGGBB" for tint, or "#RRGGBBAA" when it isn't fully opaque.</summary>
    public static string Format(Color tint) =>
        tint.A == 255 ? $"#{tint.R:X2}{tint.G:X2}{tint.B:X2}" : $"#{tint.R:X2}{tint.G:X2}{tint.B:X2}{tint.A:X2}";
}
