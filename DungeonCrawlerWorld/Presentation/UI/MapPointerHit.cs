using Engine.Math;

namespace Presentation.UI;

/// <summary>The map tile under the pointer, on the layer shown, and everything on it, topmost first.</summary>
/// <remarks>
/// Topmost is the order the map draws them, reversed: a Phasing occupant over the Blocking one, over Tiny occupants,
/// over corpses (MapDrawOrder); anything the map doesn't draw comes last. Made by MapWindow.TryGetPointerHit, the one
/// place a screen position becomes a tile and its occupants -- click selection, the right-tap menu and drops read it.
/// </remarks>
public sealed record MapPointerHit(Vector3Int Tile, int[] OccupantEntityIds);
