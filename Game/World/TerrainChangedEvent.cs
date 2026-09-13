using Game.Modules.Core.Components;

namespace Game.World;

/// <summary>
/// Published by World.PlaceTerrainOnMap whenever a cell's terrain entity is placed or replaced --
/// the signal any cache of "what does the terrain here look like" needs in order to rebuild.
/// </summary>
/// <remarks>
/// Nothing mutates terrain at runtime today: PlaceTerrainOnMap is Map.SetTerrainEntityId's only
/// caller, and TestMapBuilder (population time, before any frame is drawn) is in turn its only
/// caller. This event exists anyway, ahead of its first real producer, because
/// MapTerrainCache now renders terrain once into a texture and reuses it across frames instead
/// of re-drawing it every frame. Without an invalidation signal, the first action that ever digs
/// through a wall or spreads lava ships with an invisible bug -- the changed cell keeps drawing
/// its old terrain until something unrelated (a scroll, a zoom, a layer change) happens to
/// rebuild the cache. Cheaper to define the seam now than to discover it later as a visual
/// desync.
///
/// Immediate, not IBufferedEvent -- same reasoning as AuraSourceAddedEvent: terrain replacement
/// is rare (nothing does it during play at all yet, and a future digging/melting action is a
/// player-input-frequency event, not a per-move one), and its only consumer sets a dirty flag on
/// its own private cache rather than writing to any pool another system might be mid-scan over.
///
/// Carries the changed cell rather than nothing at all so a future consumer can invalidate a
/// region instead of everything, even though today's one (MapTerrainCache) rebuilds wholesale --
/// a full rebuild costs the same as the MapBackgroundCache.Reset that used to run on every
/// single player step, so at this event's frequency there is nothing to gain from finer
/// granularity yet.
/// </remarks>
/// <param name="X">The x-coordinate of the cell whose terrain changed.</param>
/// <param name="Y">The y-coordinate of the cell whose terrain changed.</param>
/// <param name="TerrainLayer">The terrain layer that changed at that cell.</param>
public readonly record struct TerrainChangedEvent(int X, int Y, TerrainLayer TerrainLayer);
