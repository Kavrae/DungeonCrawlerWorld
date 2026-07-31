namespace SpriteManifestBuilder;

/// <summary>
/// One candidate spritesheet region for a ManifestEntry. Mirrors Game/Blueprints/
/// SpriteManifestCell.cs's field shape/JSON property names exactly, but is a deliberately
/// separate type -- this tool has no project reference to Game.csproj, so the two must be
/// kept in sync by hand if the shape ever changes.
/// </summary>
public sealed record ManifestCell(string SheetPath, int Column, int Row, int CellWidth, int CellHeight);
