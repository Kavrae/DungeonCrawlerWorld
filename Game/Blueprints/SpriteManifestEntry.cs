namespace Game.Blueprints;

/// <summary>A named sprite, with one or more candidate visual variants -- SpriteManifest.TryGetRandom picks one at random, TryGetFirst always takes Cells[0]. Deserialized directly from Content/SpriteManifest.json, so field names/shape must stay in sync with DevTools/SpriteManifestBuilder's own independent copy of this record.</summary>
public sealed record SpriteManifestEntry(string Name, List<SpriteManifestCell> Cells);
