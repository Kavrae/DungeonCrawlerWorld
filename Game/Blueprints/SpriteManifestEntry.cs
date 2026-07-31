namespace Game.Blueprints;

/// <summary>A named sprite, with one or more candidate visual variants -- SpriteManifest.TryGet picks one at random. Deserialized directly from Content/SpriteManifest.json, so field names/shape must stay in sync with DevTools/SpriteManifestBuilder's own independent copy of this record.</summary>
public sealed record SpriteManifestEntry(string Name, List<SpriteManifestCell> Cells);
