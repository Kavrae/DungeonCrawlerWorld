namespace UIEngine.UI;

/// <summary>How a glow's strength changes over time: Static, or a Pulse that breathes for a number of cycles and then holds steady.</summary>
/// <remarks>
/// default is Static. A pulse eases on a cosine from full strength down to its minimum and back once per period, starting and ending
/// each cycle at full strength, so it hands over to the steady glow with no jump. Its time is counted from a start its holder keeps
/// (GlowPulseClock), on presentation time, so it runs while the simulation is paused. A period shorter than MinimumPulsePeriod is
/// refused: WCAG 2.3.1 allows at most three flashes a second.
/// </remarks>
public readonly record struct GlowAnimation
{
    public const int DefaultPulseCycles = 5;

    public const float DefaultPulseMinimumStrength = 0.25f;

    /// <summary>One third of a second, rounded up to the next tick: three cycles a second at most.</summary>
    public static readonly TimeSpan MinimumPulsePeriod = TimeSpan.FromTicks((TimeSpan.TicksPerSecond + 2) / 3);

    public static GlowAnimation Static => default;

    private GlowAnimation(TimeSpan pulsePeriod, float pulseMinimumStrength, int? pulseCycles)
    {
        PulsePeriod = pulsePeriod;
        PulseMinimumStrength = pulseMinimumStrength;
        PulseCycles = pulseCycles;
    }

    /// <summary>Zero for Static.</summary>
    public TimeSpan PulsePeriod { get; }

    public float PulseMinimumStrength { get; }

    /// <summary>How many cycles a pulse runs before holding steady; null for one that pulses forever.</summary>
    public int? PulseCycles { get; }

    public bool IsPulsing => PulsePeriod > TimeSpan.Zero;

    public bool PulsesForever => IsPulsing && PulseCycles is null;

    /// <summary>A pulse of the given period; cycles null or int.MaxValue pulses forever.</summary>
    /// <remarks>A NaN minimumStrength is taken as 0.</remarks>
    public static GlowAnimation Pulse(TimeSpan period, float minimumStrength = DefaultPulseMinimumStrength, int? cycles = DefaultPulseCycles)
    {
        if (float.IsNaN(minimumStrength))
        {
            minimumStrength = 0f;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(period, MinimumPulsePeriod);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumStrength, 0f);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumStrength, 1f);
        if (cycles is { } cycleCount)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(cycleCount, 1, nameof(cycles));
        }

        return new GlowAnimation(period, minimumStrength, cycles == int.MaxValue ? null : cycles);
    }

    /// <summary>The glow's strength, 0 to 1, this long after its pulse started.</summary>
    public float StrengthAt(TimeSpan elapsedSincePulseStart)
    {
        if (!IsPulsing)
        {
            return 1f;
        }

        var elapsedTicks = System.Math.Max(0L, elapsedSincePulseStart.Ticks);
        var periodTicks = PulsePeriod.Ticks;
        if (PulseCycles is { } cycles && elapsedTicks / periodTicks >= cycles)
        {
            return 1f;
        }

        var phase = (double)(elapsedTicks % periodTicks) / periodTicks;
        var wave = 0.5 + 0.5 * System.Math.Cos(2 * System.Math.PI * phase);
        return (float)(PulseMinimumStrength + (1 - PulseMinimumStrength) * wave);
    }
}
