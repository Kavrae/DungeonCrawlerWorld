using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>One corner of a quad SpriteEffectBatch draws: SpriteBatch's position, colour and texture coordinate, plus the draw's style for the sprite effect.</summary>
/// <remarks>Its layout is half of a contract whose other half is Shaders/SpriteUber.fx's input; Version is what an effect declares as VertexVersion, so an effect built for another layout is refused (SpriteEffectService).</remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SpriteEffectVertex : IVertexType
{
    public const float Version = 3f;

    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        new VertexElement(16, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(24, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 1));

    public Vector3 Position;

    /// <summary>Tint times alpha, premultiplied -- what SpriteBatch takes as a draw's colour.</summary>
    public Color Color;

    public Vector2 TextureCoordinate;

    /// <summary>SpriteDrawStyle.ToVertexStyle: the mode, the glow, the ground row's y, and whether it is a glyph's outline layer.</summary>
    public Vector4 Style;

    readonly VertexDeclaration IVertexType.VertexDeclaration => Declaration;
}
