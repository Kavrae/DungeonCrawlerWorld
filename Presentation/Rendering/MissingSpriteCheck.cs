using Game.Views;
using UIEngine.Rendering;

namespace Presentation.Rendering;

/// <summary>Reports every sprite name the content declares that the loaded manifest doesn't have, once each, at startup.</summary>
/// <remarks>
/// A missing sprite is not a failure: whatever names it draws its glyph, and a mod's content can name art the manifest hasn't been
/// given yet. Game can't check this (it never sees the manifest), so Presentation does, once the session and the manifest both exist.
/// </remarks>
public static class MissingSpriteCheck
{
    /// <summary>Writes one line to output per declared sprite name the manifest lacks, and returns those names.</summary>
    public static IReadOnlyList<DeclaredSpriteName> Report(ContentSpriteNames contentSpriteNames, SpriteManifestResolver sprites, TextWriter output)
    {
        var missing = contentSpriteNames.Collect().Where(declared => !sprites.Contains(declared.SpriteName)).ToList();
        foreach (var declared in missing)
        {
            output.WriteLine($"Sprite '{declared.SpriteName}' ({declared.DeclaredBy}) is not in the sprite manifest; it draws its glyph.");
        }

        return missing;
    }
}
