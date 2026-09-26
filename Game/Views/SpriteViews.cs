using Game.Sprites;

namespace Game.Views;

/// <summary>Sprite lookups by manifest name, as SpriteView.</summary>
public static class SpriteViews
{
    /// <summary>The first cell listed under name in SpriteManifest -- the stable choice, for a sprite that must look the same everywhere it's drawn.</summary>
    public static bool TryGetFirst(string name, out SpriteView sprite)
    {
        if (SpriteManifest.TryGetFirst(name, out var resolved))
        {
            sprite = new SpriteView(resolved.SheetPath, resolved.SourceRectangle);
            return true;
        }

        sprite = default;
        return false;
    }
}
