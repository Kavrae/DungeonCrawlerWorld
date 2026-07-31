namespace Game.Blueprints;

/// <summary>One candidate spritesheet region for a SpriteManifestEntry -- plain data, matching what SpriteComponent.FromCell needs.</summary>
public sealed record SpriteManifestCell(string SheetPath, int Column, int Row, int CellWidth, int CellHeight);
