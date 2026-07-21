namespace Game.Modules.Core.Components;

/// <summary>
/// The real vertical space an entity occupies on the map -- distinct from OccupancyComponent,
/// which governs whether entities can coexist within a layer (Blocking/Tiny/Phasing). Flying
/// has no terrain layer beneath it (see TerrainLayer) -- it's open air.
/// </summary>
public enum MapLayer : byte
{
    UnderGround = 0,
    Ground = 1,
    Flying = 2,
}
