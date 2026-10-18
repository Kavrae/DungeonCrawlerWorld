using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>A spritesheet a sprite is cut from: its path under the spritesheets folder, and its texture once SpriteSheetService has loaded it.</summary>
/// <remarks>
/// The manifest resolver makes one per distinct path and every sprite on that sheet shares it, so drawing reads the texture
/// from here rather than looking the path up. Two sheets are equal when their paths are, whichever instance holds the texture.
/// </remarks>
public sealed class SpriteSheet(string relativePath) : IEquatable<SpriteSheet>
{
    /// <summary>The sheet's path, relative to the spritesheets folder.</summary>
    public string RelativePath { get; } = relativePath;

    /// <summary>The loaded texture; null until SpriteSheetService.GetTexture first loads it.</summary>
    internal Texture2D? Texture { get; set; }

    public bool Equals(SpriteSheet? other) => other is not null && (ReferenceEquals(this, other) || string.Equals(RelativePath, other.RelativePath, StringComparison.Ordinal));

    public override bool Equals(object? obj) => Equals(obj as SpriteSheet);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(RelativePath);

    public override string ToString() => RelativePath;
}
