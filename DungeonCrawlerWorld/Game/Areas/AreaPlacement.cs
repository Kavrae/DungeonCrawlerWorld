namespace Game.Areas;

/// <summary>A fixed spot in the world one area is always stamped at: a neighborhood and the offset of the area's top-left tile in it.</summary>
/// <param name="Area">The AreaDefinition placed.</param>
/// <param name="NeighborhoodX">The neighborhood's cell (Game.World.Neighborhoods).</param>
/// <param name="OffsetX">The area's left column within the neighborhood.</param>
/// <param name="OffsetY">The area's top row within the neighborhood.</param>
public sealed record AreaPlacement(Guid Id, Guid Area, int NeighborhoodX, int NeighborhoodY, int OffsetX, int OffsetY);
