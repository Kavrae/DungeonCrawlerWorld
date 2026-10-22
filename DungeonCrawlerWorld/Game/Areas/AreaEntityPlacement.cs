using Engine.Math;
using Game.Modules.Core.Components;

namespace Game.Areas;

/// <summary>One blueprint placed at one tile of an area, with an id later content can name it by.</summary>
/// <remarks>
/// A Unique placement spawns its blueprint only when none of it is alive in the session and none has been killed there
/// (UniqueEntityRoster); otherwise it spawns Fallback, if any. A unique is a blueprint with its own Name: a name per
/// placement wouldn't rebuild from the spawn record.
/// </remarks>
/// <param name="PlacementId">Unique within the area; never its position in the list.</param>
/// <param name="X">The footprint's left column, as a SpawnRequest's.</param>
/// <param name="Y">The footprint's top row.</param>
/// <param name="Size">The footprint; null for the blueprint's own.</param>
/// <param name="Chance">The percent chance it spawns at all, 1 to 100.</param>
/// <param name="Fallback">What a Unique placement spawns instead when its blueprint is alive elsewhere or was killed; only for a Unique one.</param>
/// <param name="Description">Authoring notes: why it is there.</param>
public sealed record AreaEntityPlacement(
    string PlacementId,
    int X,
    int Y,
    Guid Blueprint,
    MapLayer Layer = MapLayer.Ground,
    Vector2Byte? Size = null,
    byte Chance = AreaSpawn.AlwaysChance,
    bool Unique = false,
    Guid? Fallback = null,
    string? Description = null);
