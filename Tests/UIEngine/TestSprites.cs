using UIEngine.Rendering;

namespace Tests.UIEngine;

/// <summary>The checked-in sprite manifest, loaded once from the test output, for every test that builds a SpriteSheetService.</summary>
public static class TestSprites
{
    private static readonly Lazy<SpriteManifestResolver> LoadedResolver =
        new(() => SpriteManifestResolver.Load(Path.Combine(AppContext.BaseDirectory, "SpriteManifest.json")));

    public static SpriteManifestResolver Resolver => LoadedResolver.Value;
}
