namespace Engine.Tiles;

/// <summary>What a Projectile or Lobbed path does on reaching a wall.</summary>
public enum PathWallRule : byte
{
    /// <summary>Stops on the last open tile before the wall.</summary>
    StopsAtWall,

    /// <summary>Goes on through the wall.</summary>
    PassesThroughWalls,

    /// <summary>Reflects off the wall, up to the targeting's bounce count.</summary>
    BouncesOffWalls,
}
