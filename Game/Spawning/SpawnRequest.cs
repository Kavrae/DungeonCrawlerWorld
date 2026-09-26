using Engine.Math;
using Game.Modules.Core.Components;

namespace Game.Spawning;

/// <summary>One entity to spawn: which blueprint and where, with everything else defaulting to what the blueprint says.</summary>
/// <remarks>
/// Only the blueprint and the cell are required. Layer and Size default to the blueprint's own
/// (ResolvedBlueprint.Layer/Size), so a fairy spawns on the Flying layer unless the spawn puts it
/// elsewhere. Seed defaults to the factory's runtime sequence; population passes its own so a
/// neighborhood regenerates exactly.
/// </remarks>
/// <param name="BlueprintId">The BlueprintRegistry id to spawn.</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct SpawnRequest(ushort BlueprintId, int X, int Y)
{
    /// <summary>The MapLayer to spawn on, or null for the blueprint's.</summary>
    public MapLayer? Layer { get; init; }

    /// <summary>The footprint to place it with, or null for the blueprint's.</summary>
    public Vector2Byte? Size { get; init; }

    /// <summary>The seed every random choice its build makes is drawn from, or null for the next from the factory's runtime sequence.</summary>
    public uint? Seed { get; init; }

    /// <summary>Whether it is also a Crawler: flagged in its spawn record, and given the session's next crawler number when it is first built. Ignored once the session's crawler numbers are exhausted.</summary>
    public bool Crawler { get; init; }

    /// <summary>An id the caller already minted to spawn into (the player's reserved one), or null for a new entity.</summary>
    public int? ReservedEntityId { get; init; }

    /// <summary>A request at position, on position's own layer.</summary>
    public static SpawnRequest At(ushort blueprintId, Vector3Int position) =>
        new(blueprintId, position.X, position.Y) { Layer = (MapLayer)position.Z };
}
