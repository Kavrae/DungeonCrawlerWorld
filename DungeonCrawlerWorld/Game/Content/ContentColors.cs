using Engine.Content;
using Microsoft.Xna.Framework;

namespace Game.Content;

/// <summary>Colours in content files: the packed RGBA value (Color.PackedValue, red in the low byte), written as an unsigned integer and omitted when white.</summary>
public static class ContentColors
{
    public static uint Pack(Color color) => color.PackedValue;

    public static Color Unpack(uint packed) =>
        new((byte)(packed & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)((packed >> 16) & 0xFF), (byte)(packed >> 24));

    public static void Color(this ContentWriter writer, string key, Color color) =>
        writer.UnsignedInteger(key, Pack(color), Pack(Microsoft.Xna.Framework.Color.White));

    public static Color Color(this ContentObject element, string key) =>
        Unpack(element.UnsignedInteger(key, Pack(Microsoft.Xna.Framework.Color.White)));
}
