namespace SpriteManifestBuilder;

/// <summary>Mirrors Game/Blueprints/SpriteManifestEntry.cs's shape -- see ManifestCell's own doc comment for why this is a separate, independently-maintained type.</summary>
public sealed record ManifestEntry(string Name, List<ManifestCell> Cells);
