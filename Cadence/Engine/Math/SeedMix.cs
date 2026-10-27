namespace Engine.Math;

/// <summary>Derives a seed from stable values -- other seeds, salts, coordinates, Guids and names -- instead of drawing one from a sequence.</summary>
/// <remarks>
/// A derived seed depends only on what it is mixed from, never on what else was rolled first, so anything generated from
/// it is the same whatever order things are asked for in. Deterministic across processes and runs, unlike HashCode and
/// string.GetHashCode. Mix is SplitMix64's finalizer; strings are folded with FNV-1a.
/// </remarks>
public static class SeedMix
{
    private const ulong FnvOffsetBasis = 0xCBF29CE484222325;
    private const ulong FnvPrime = 0x100000001B3;

    /// <summary>The two values combined into one well-mixed seed: changing either changes every bit about half the time.</summary>
    public static ulong Mix(ulong first, ulong second)
    {
        var value = first + 0x9E3779B97F4A7C15 * (second + 1);
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EB;
        return value ^ (value >> 31);
    }

    /// <summary>A 32-bit seed (an int session or layout seed) widened without sign extension, so -1 and uint.MaxValue mix the same.</summary>
    public static ulong Of(int seed) => (uint)seed;

    /// <summary>Two 32-bit values packed into one, high then low -- a coordinate pair, say.</summary>
    public static ulong Pack(int high, int low) => ((ulong)(uint)high << 32) | (uint)low;

    public static ulong Of(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes);
        return Mix(BitConverter.ToUInt64(bytes[..8]), BitConverter.ToUInt64(bytes[8..]));
    }

    /// <summary>FNV-1a over the text's characters: stable across runs, unlike string.GetHashCode.</summary>
    public static ulong Of(string text)
    {
        var hash = FnvOffsetBasis;
        foreach (var character in text)
        {
            hash = (hash ^ character) * FnvPrime;
        }

        return hash;
    }
}
