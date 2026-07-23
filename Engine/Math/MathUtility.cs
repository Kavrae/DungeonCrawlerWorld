namespace Engine.Math;

/// <summary>
/// Integer/short clamping (FNA's MathHelper.Clamp is float-only) plus a random-selection
/// helper used on hot paths like movement direction selection.
/// </summary>
public sealed class MathUtility(Random? randomizer = null)
{
    private readonly Random _randomizer = randomizer ?? new Random();

    public static int ClampInt(int value, int min, int max)
    {
        if (min > max)
        {
            throw new ArgumentException($"min ({min}) is greater than max ({max}).");
        }

        value = value > max
            ? max
            : value;
        value = value < min
            ? min
            : value;
        return value;
    }

    public static short ClampShort(short value, short min, short max)
    {
        if (min > max)
        {
            throw new ArgumentException($"min ({min}) is greater than max ({max}).");
        }

        value = value > max
            ? max
            : value;
        value = value < min
            ? min
            : value;
        return value;
    }

    /// <summary>Clamps value into byte's own range instead of wrapping on cast, e.g. for arithmetic that may overflow/underflow byte.</summary>
    public static byte ClampByte(int value) => (byte)ClampInt(value, byte.MinValue, byte.MaxValue);

    /// <summary>
    /// Randomly selects a value in [0, maximumValue) that does not appear in
    /// valuesToSkip, with every remaining value equally likely. Uses rejection sampling:
    /// draw a candidate, retry if it's excluded. valuesToSkip may be in any order and may
    /// contain duplicates -- selection has no ordering requirement and no positional bias.
    /// </summary>
    public int RandomExceptFor(int maximumValue, ReadOnlySpan<int> valuesToSkip)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(valuesToSkip.Length, maximumValue);

        while (true)
        {
            var candidate = _randomizer.Next(maximumValue);
            if (!valuesToSkip.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// A random integer in [minValue, maxValue). Passthrough to the wrapped Random instance
    /// so callers needing general random values (e.g. randomized starting stats) don't need
    /// a second, separately-seeded Random alongside this one.
    /// </summary>
    public int Next(int minValue, int maxValue) => _randomizer.Next(minValue, maxValue);
}