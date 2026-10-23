using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>One corner of a quad SpriteEffectBatch draws: SpriteBatch's position, colour and texture coordinate, plus the draw's style for the sprite effect.</summary>
/// <remarks>Its layout is half of a contract whose other half is Shaders/SpriteUber.fx's input; Version is what an effect declares as VertexVersion, so an effect built for another layout is refused (SpriteEffectService).</remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SpriteEffectVertex : IVertexType
{
    public const float Version = 4f;

    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
        new VertexElement(8, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        new VertexElement(12, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(20, VertexElementFormat.Color, VertexElementUsage.TextureCoordinate, 1),
        new VertexElement(24, VertexElementFormat.Single, VertexElementUsage.TextureCoordinate, 2));

    public Vector2 Position;

    /// <summary>Tint times alpha, premultiplied -- what SpriteBatch takes as a draw's colour.</summary>
    public Color Color;

    public Vector2 TextureCoordinate;

    /// <summary>SpriteDrawStyle.ToVertexStyle: the mode, the glow, whether it is a glyph's outline layer, and the ground row's y.</summary>
    public SpriteEffectVertexStyle Style;

    readonly VertexDeclaration IVertexType.VertexDeclaration => Declaration;
}

/// <summary>A SpriteDrawStyle as SpriteEffectVertex carries it: three bytes the shader reads normalized (a Color element), a padding byte, and the ground row's y.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct SpriteEffectVertexStyle(byte Mode, byte Glow, byte OutlineOnly, byte Unused, float GroundRowY);
