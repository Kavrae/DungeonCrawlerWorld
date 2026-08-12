namespace Engine.Math;

/// <summary>
/// Math utility functions that are missing from the FNA framework.
/// </summary>
public sealed class MathUtility(Random? randomizer = null)
{
    /// <summary>
    /// Seeded randomizer for deterministic testing.
    /// </summary>
    private readonly Random _randomizer = randomizer ?? new Random();

    /// <summary>
    /// Clamps an integer value between a minimum and maximum value, inclusive.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="min"></param>
    /// <param name="max"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
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

    /// <summary>
    /// Clamps a short value between a minimum and maximum value, inclusive.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="min"></param>
    /// <param name="max"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
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

    /// <summary>
    /// Clamps an integer value into the range of a byte (0 to 255) instead of wrapping on cast, e.g. for arithmetic that may overflow/underflow byte.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static byte ClampByte(int value) => (byte)ClampInt(value, byte.MinValue, byte.MaxValue);

    /// <summary>
    /// Randomly selects a value in [0, maximumValue) that does not appear in
    /// valuesToSkip, with every remaining value equally likely. Uses rejection sampling:
    /// draw a candidate, retry if it's excluded. valuesToSkip may be in any order and may
    /// contain duplicates -- selection has no ordering requirement and no positional bias.
    /// </summary>
    /// <param name="maximumValue"></param>
    /// <param name="valuesToSkip"></param>
    /// <returns></returns>
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
    /// <param name="minValue"></param>
    /// <param name="maxValue"></param>
    /// <returns></returns>
    public int Next(int minValue, int maxValue) => _randomizer.Next(minValue, maxValue);

    /// <summary>A random double in [0.0, 1.0) -- passthrough for probability rolls (e.g. crit chance, proc chance) that Next(int, int) can't express directly.</summary>
    /// <returns></returns>
    public double NextDouble() => _randomizer.NextDouble();
}