using Game.World;

namespace Game.Floors;

/// <summary>Published by NeighborhoodStreamer as a neighborhood starts to unload, before anything in it is destroyed: from here until NeighborhoodUnloadedEvent, an entity destroyed there is destroyed by the unload.</summary>
/// <remarks>Consumer: ItemPileKeeping, which keeps each item pile the unload destroys in the record.</remarks>
public readonly record struct NeighborhoodUnloadingEvent(int CellX, int CellY, NeighborhoodRecord Record);

/// <summary>Published by NeighborhoodStreamer once a neighborhood's unload is done: everything in it destroyed and its stores dropped.</summary>
/// <remarks>Consumer: ItemPileKeeping, which stops keeping piles for it.</remarks>
public readonly record struct NeighborhoodUnloadedEvent(int CellX, int CellY);

/// <summary>Published by NeighborhoodStreamer once a loading neighborhood's layout and population are in place: the moment to put back what its record kept.</summary>
/// <remarks>Consumer: ItemPileKeeping, which restores the item piles the record kept.</remarks>
public readonly record struct NeighborhoodPopulatedEvent(int CellX, int CellY, NeighborhoodRecord Record);
