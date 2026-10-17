namespace Engine.Utilities;

/// <summary>The host's fixed update rate, and conversions between seconds and frames at it.</summary>
/// <remarks>
/// The engine holds the rate; the game decides it, by calling SetFramesPerSecond at startup before anything converts a
/// duration. The first read fixes it: setting a different rate afterwards throws, since every duration already converted
/// would disagree with it, and reading it before it is set throws rather than assuming a rate.
/// </remarks>
/// <cleanupVersion>2</cleanupVersion>
public static class GameTiming
{
    private static ushort _framesPerSecond;
    private static bool _isFixed;

    /// <summary>How many update frames make one second.</summary>
    /// <exception cref="InvalidOperationException">The game hasn't set it yet.</exception>
    public static ushort FramesPerSecond
    {
        get
        {
            if (_framesPerSecond == 0)
            {
                throw new InvalidOperationException("GameTiming.FramesPerSecond was read before the game set it; call GameTiming.SetFramesPerSecond at startup.");
            }

            _isFixed = true;
            return _framesPerSecond;
        }
    }

    /// <summary>Sets the update rate. Setting the rate it already has does nothing.</summary>
    /// <exception cref="InvalidOperationException">A different rate was already read.</exception>
    public static void SetFramesPerSecond(ushort framesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfZero(framesPerSecond);

        if (_isFixed && framesPerSecond != _framesPerSecond)
        {
            throw new InvalidOperationException($"GameTiming.FramesPerSecond is already {_framesPerSecond} and has been read; it can't become {framesPerSecond}.");
        }

        _framesPerSecond = framesPerSecond;
    }

    /// <summary>Converts a real-world duration to a frame count at FramesPerSecond, rounded to the nearest frame.</summary>
    public static ushort FramesForSeconds(float seconds) => (ushort)MathF.Round(seconds * FramesPerSecond);
}
