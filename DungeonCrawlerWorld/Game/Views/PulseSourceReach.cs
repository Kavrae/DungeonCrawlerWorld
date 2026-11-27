using Engine.Math;
using Engine.Tiles;

namespace Game.Views;

/// <summary>One pulse source reaching an area: where it radiates from (its carrier's source tile), its aura, power, reach and falloff -- what its value at any tile there follows from.</summary>
public readonly record struct PulseSourceReach(byte AuraId, Vector3Int Center, ushort Power, byte Size, AuraFalloff Falloff)
{
    /// <summary>The source's value at a tile on its layer: its falloff at the Manhattan distance, or 0 past its reach.</summary>
    public int ValueAt(int x, int y)
    {
        var distance = Math.Abs(x - Center.X) + Math.Abs(y - Center.Y);
        return distance <= Size ? Falloff.ValueAt(Power, Size, distance) : 0;
    }
}
