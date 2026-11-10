using Engine.Math;
using Game.Modules.Actions;

namespace Tests;

/// <summary>Targeting selections for tests that queue an activation by hand.</summary>
internal static class TestSelections
{
    /// <summary>Range wide enough that no test map puts a tile out of it.</summary>
    private const ushort AnyRange = 1000;

    /// <summary>A Ground selection of tile: the activation lands on tile, with the given area, wherever its caster stands.</summary>
    public static TargetSelection At(Vector3Int tile, int areaSize = 0) =>
        TargetSelection.Ground(tile) with { MaximumRange = AnyRange, AreaSize = (ushort)areaSize };

    /// <summary>A Target-mode selection of tile with nothing marked: lands as At does, but a toggle switched on with it keeps what it places on its holder.</summary>
    public static TargetSelection TargetModeAt(Vector3Int tile) => At(tile) with { Mode = TargetingMode.Target };
}
