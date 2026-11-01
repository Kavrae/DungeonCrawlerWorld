namespace Engine.Tiles;

/// <summary>Where a projectile's path ends when nothing stops it earlier.</summary>
public enum ProjectilePathEnd : byte
{
    /// <summary>On the aimed tile.</summary>
    AtAimedTile,

    /// <summary>Past the aimed tile, at maximum range.</summary>
    AtMaximumRange,
}
