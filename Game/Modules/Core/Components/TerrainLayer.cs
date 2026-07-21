namespace Game.Modules.Core.Components;

/// <summary>
/// The floor beneath a MapLayer -- independent storage from creature occupancy, since
/// terrain (dirt, grass, stone, lava) never blocks movement and must coexist with whatever
/// creature is standing on it. Flying has no floor (open air), so there is no third value.
/// </summary>
public enum TerrainLayer : byte
{
    UnderGround = 0,
    Ground = 1,
}
