namespace Engine.Math;

/// <summary>Hands out every integer in [minValue, minValue + 2^valueBits) exactly once, in a seeded shuffled order.</summary>
/// <remarks>
/// A balanced 4-round Feistel network keyed from the seed permutes a counter, so each allocation is
/// O(1), nothing is stored beyond the counter, and the order is fixed by the seed. valueBits must be
/// even so the two Feistel halves are the same width.
/// </remarks>
/// <cleanupVersion>2</cleanupVersion>
public sealed class UniqueNumberAllocator
{
    private const int RoundCount = 4;

    private readonly int _minValue;
    private readonly int _halfBits;
    private readonly ulong _halfMask;
    private readonly ulong _count;
    private readonly ulong[] _roundKeys = new ulong[RoundCount];

    private ulong _issued;

    public UniqueNumberAllocator(ulong seed, int minValue, int valueBits)
    {
        if (valueBits is < 2 or > 30 || valueBits % 2 != 0)
        {
            throw new ArgumentException($"valueBits ({valueBits}) must be even and between 2 and 30.", nameof(valueBits));
        }

        _count = 1UL << valueBits;
        if ((long)minValue + (long)_count - 1 > int.MaxValue)
        {
            throw new ArgumentException($"minValue ({minValue}) + 2^{valueBits} - 1 exceeds int.MaxValue.", nameof(minValue));
        }

        _minValue = minValue;
        _halfBits = valueBits / 2;
        _halfMask = (1UL << _halfBits) - 1;

        for (var round = 0; round < RoundCount; round++)
        {
            _roundKeys[round] = SplitMix64(ref seed);
        }
    }

    /// <summary>Whether every number in the range has been handed out.</summary>
    public bool IsExhausted => _issued == _count;

    /// <summary>Hands out the next number, or returns false once every number in the range has been handed out.</summary>
    public bool TryAllocate(out int number)
    {
        if (IsExhausted)
        {
            number = 0;
            return false;
        }

        number = _minValue + (int)Permute(_issued++);
        return true;
    }

    private ulong Permute(ulong value)
    {
        var left = value >> _halfBits;
        var right = value & _halfMask;

        foreach (var key in _roundKeys)
        {
            (left, right) = (right, left ^ (Mix(right ^ key) & _halfMask));
        }

        return (left << _halfBits) | right;
    }

    private static ulong SplitMix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        return Mix(state);
    }

    private static ulong Mix(ulong value)
    {
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
