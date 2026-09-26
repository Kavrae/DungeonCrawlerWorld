namespace Engine.Math;

/// <summary>A Random that can be reseeded in place, so one instance can replay any seed's sequence without allocating.</summary>
/// <remarks>
/// xoshiro256** seeded through SplitMix64. System.Random(seed) allocates its whole state on every
/// construction, and a sequence per creature (see EntityFactory) would mean one per spawn. The
/// sequence for a seed is fixed by this class alone, not by the runtime's Random implementation.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class SeededRandom : Random
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    public SeededRandom(ulong seed = 0) => Reseed(seed);

    /// <summary>Restarts the sequence from seed.</summary>
    public void Reseed(ulong seed)
    {
        _s0 = SplitMix64(ref seed);
        _s1 = SplitMix64(ref seed);
        _s2 = SplitMix64(ref seed);
        _s3 = SplitMix64(ref seed);
    }

    public override int Next() => (int)(NextUInt64() >> 33);

    public override int Next(int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return (int)NextBelow((ulong)maxValue);
    }

    public override int Next(int minValue, int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        return (int)((long)minValue + (long)NextBelow((ulong)((long)maxValue - minValue)));
    }

    public override long NextInt64() => (long)(NextUInt64() >> 1);

    public override long NextInt64(long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return (long)NextBelow((ulong)maxValue);
    }

    public override long NextInt64(long minValue, long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        return minValue + (long)NextBelow((ulong)(maxValue - minValue));
    }

    public override double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public override float NextSingle() => (NextUInt64() >> 40) * (1.0f / (1U << 24));

    public override void NextBytes(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        NextBytes(buffer.AsSpan());
    }

    public override void NextBytes(Span<byte> buffer)
    {
        while (buffer.Length >= sizeof(ulong))
        {
            BitConverter.TryWriteBytes(buffer, NextUInt64());
            buffer = buffer[sizeof(ulong)..];
        }

        if (!buffer.IsEmpty)
        {
            Span<byte> last = stackalloc byte[sizeof(ulong)];
            BitConverter.TryWriteBytes(last, NextUInt64());
            last[..buffer.Length].CopyTo(buffer);
        }
    }

    protected override double Sample() => NextDouble();

    /// <summary>The next raw 64-bit value of the sequence.</summary>
    public ulong NextUInt64()
    {
        var result = RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotateLeft(_s3, 45);

        return result;
    }

    /// <summary>Uniform in [0, bound), without modulo bias (Lemire's multiply-and-reject). 0 when bound is 0.</summary>
    private ulong NextBelow(ulong bound)
    {
        if (bound == 0)
        {
            return 0;
        }

        var product = System.Math.BigMul(NextUInt64(), bound, out var low);
        if (low < bound)
        {
            var threshold = (0 - bound) % bound;
            while (low < threshold)
            {
                product = System.Math.BigMul(NextUInt64(), bound, out low);
            }
        }

        return product;
    }

    private static ulong SplitMix64(ref ulong state)
    {
        var z = state += 0x9E3779B97F4A7C15;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
        return z ^ (z >> 31);
    }

    private static ulong RotateLeft(ulong value, int count) => (value << count) | (value >> (64 - count));
}
