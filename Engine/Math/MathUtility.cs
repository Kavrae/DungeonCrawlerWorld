namespace Engine.Math;

/// <summary>
/// Math utility functions that are missing from the FNA framework.
/// </summary>
/// <param name="randomizer">Optional seeded randomizer for deterministic testing. If null, a new Random instance will be created.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed class MathUtility(Random? randomizer = null)
{
    private readonly Random _randomizer = randomizer ?? new Random();

    /// <summary>
    /// Clamps an integer value between a minimum and maximum value, inclusive.
    /// </summary>
    /// <param name="value">The integer value to clamp.</param>
    /// <param name="min">The minimum value.</param>
    /// <param name="max">The maximum value.</param>
    /// <returns>The clamped integer value.</returns>
    /// <exception cref="ArgumentException">If min is greater than max.</exception>
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
    /// Clamps a ushort value between a minimum and maximum value, inclusive.
    /// </summary>
    /// <param name="value">The ushort value to clamp.</param>
    /// <param name="min">The minimum value.</param>
    /// <param name="max">The maximum value.</param>
    /// <returns>The clamped ushort value.</returns>
    /// <exception cref="ArgumentException">If min is greater than max.</exception>
    public static ushort ClampUShort(ushort value, ushort min, ushort max)
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
    /// Clamps a float value into a ushort's range before narrowing to it. Casting an out-of-range
    /// float to ushort directly does not saturate -- it truncates/wraps to an unspecified value
    /// (e.g. a large enough float silently wraps modulo 65536 instead of clamping to
    /// ushort.MaxValue) -- so callers computing a ushort from a float that could be negative or
    /// too large (e.g. StatModifierMath.GetEffectiveValue's modifier-adjusted result) must clamp
    /// in float space first, not cast first and clamp after.
    /// </summary>
    public static ushort ClampUShort(float value, ushort min, ushort max)
    {
        if (min > max)
        {
            throw new ArgumentException($"min ({min}) is greater than max ({max}).");
        }

        return (ushort)System.Math.Clamp(value, min, max);
    }

    /// <summary>
    /// Clamps an integer value into the range of a byte (0 to 255) instead of wrapping on cast, e.g. for arithmetic that may overflow/underflow byte.
    /// </summary>
    /// <param name="value">The integer value to clamp.</param>
    /// <returns>The clamped byte value.</returns>
    public static byte ClampByte(int value) => (byte)ClampInt(value, byte.MinValue, byte.MaxValue);

    /// <summary>Decrements a countdown value by step, floored at 0.</summary>  
    public static ushort DecrementClamped(ushort value, ushort step) => (ushort)System.Math.Max(0, value - step);

    /// <summary>Blends between two endpoints by a normalized fraction.</summary>
    /// <remarks>
    /// No clamping or domain-range normalization -- normalizedFraction is assumed already in
    /// [0, 1], since callers with their own domain range (e.g. AbilityScoreMath.Lerp, clamping a
    /// short into [1, 300]) typically want to cache their own normalization reciprocal rather
    /// than recompute a division here on every call. atMin may be greater than atMax -- a caller
    /// whose output should shrink as normalizedFraction rises just passes its endpoints in that
    /// order.
    /// </remarks>
    /// <param name="normalizedFraction">A value in [0, 1] representing the interpolation fraction.</param>
    /// <param name="atMin">The minimum value.</param>
    /// <param name="atMax">The maximum value.</param>
    /// <returns>The interpolated value.</returns>
    public static float Lerp(float normalizedFraction, float atMin, float atMax) => atMin + normalizedFraction * (atMax - atMin);

    /// <summary> Randomly selects a value in [0, maximumValue) that does not appear in valuesToSkip</summary>
    /// <remarks>
    /// Every remaining value is equally likely via rejection sampling.
    /// </remarks>
    /// <param name="maximumValue">The maximum value.</param>
    /// <param name="valuesToSkip">The values to skip.</param>
    /// <returns>The randomly selected value.</returns>
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

    /// <summary> A random integer in [minValue, maxValue)</summary>
    /// <remarks>Passthrough for deterministic randomization.</remarks>
    /// <param name="minValue">The minimum value.</param>
    /// <param name="maxValue">The maximum value.</param>
    /// <returns>The randomly selected value.</returns>
    public int Next(int minValue, int maxValue) => _randomizer.Next(minValue, maxValue);

    /// <summary>A random double in [0.0, 1.0)</summary>
    /// <remarks>Passthrough for deterministic randomization.</remarks>
    public double NextDouble() => _randomizer.NextDouble();
}