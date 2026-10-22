namespace Game.Areas;

/// <summary>Symbol meanings shared by many areas (a stone wall on stone floor), which an area names in its Palettes.</summary>
public sealed record AreaPalette(Guid Id, string Name, IReadOnlyList<AreaCell> Legend);
