using Engine.Math;
using Microsoft.Xna.Framework.Input;

namespace Presentation.UI;

/// <summary>What an aimed tile means to a Dodge, for a caster of any footprint: a step of the whole footprint one tile toward it, and the tile each WASD key aims at.</summary>
/// <remarks>
/// The targetable tiles (ActionTargetingController.KeepDodgeStepTiles), the step queued on confirm, the WASD keys and their
/// hint letters (MapGameOverlay) all read this, so what is drawn is what a key or a click does.
/// </remarks>
public static class DodgeFootprint
{
    /// <summary>The keys that confirm a Dodge in a cardinal direction, with the letter drawn on the tile each aims at.</summary>
    public static readonly (Keys Key, string Label, Vector3Int Direction)[] DirectionalKeys =
    [
        (Keys.W, "W", new Vector3Int(0, -1, 0)),
        (Keys.S, "S", new Vector3Int(0, 1, 0)),
        (Keys.A, "A", new Vector3Int(-1, 0, 0)),
        (Keys.D, "D", new Vector3Int(1, 0, 0)),
    ];

    /// <summary>The one-tile step toward aimedTile from the footprint at footprintPosition: each axis -1, 0 or 1 by which side of the footprint the tile lies on. Null for a tile inside the footprint, a Dodge in place.</summary>
    public static Vector3Int? StepDirection(Vector3Int aimedTile, Vector3Int footprintPosition, Vector2Byte footprintSize)
    {
        var stepX = StepAlong(aimedTile.X, footprintPosition.X, footprintSize.X);
        var stepY = StepAlong(aimedTile.Y, footprintPosition.Y, footprintSize.Y);
        return stepX == 0 && stepY == 0 ? null : new Vector3Int(stepX, stepY, 0);
    }

    /// <summary>The tile just outside the footprint's side in cardinal direction that its key aims at and its letter is drawn on: the side's middle tile, or for an even size the first of its two middles (the left, or the upper).</summary>
    public static Vector3Int SideTile(Vector3Int direction, Vector3Int footprintPosition, Vector2Byte footprintSize) =>
        new(SideAlong(direction.X, footprintPosition.X, footprintSize.X), SideAlong(direction.Y, footprintPosition.Y, footprintSize.Y), footprintPosition.Z);

    private static int StepAlong(int tile, int footprintStart, int footprintLength) =>
        tile < footprintStart ? -1 : tile >= footprintStart + footprintLength ? 1 : 0;

    private static int SideAlong(int step, int footprintStart, int footprintLength) =>
        step < 0 ? footprintStart - 1 : step > 0 ? footprintStart + footprintLength : footprintStart + (footprintLength - 1) / 2;
}
