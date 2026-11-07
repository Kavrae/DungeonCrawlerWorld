namespace UIEngine.UI;

/// <summary>When a glow's current pulse started, for GlowAnimation.StrengthAt.</summary>
/// <remarks>
/// Restart marks it and the draw takes the start with StartPendingAt, at its own time, since a restart usually comes from input or
/// event handling with no clock to hand. Clocks restarted in the same update and drawn in the same frame therefore pulse in step.
/// A glow that is not drawn -- inside a minimized parent, say -- starts its pulse when it is first drawn, so it pulses when the player
/// first sees it: a badge in a minimized Folder pulses when the Folder is opened, after the Folder's own pulse has settled. A clock that
/// has never started starts at its first StartPendingAt. ElapsedAt only reads: a pulse not yet started reads as just starting.
/// </remarks>
public struct GlowPulseClock
{
    private TimeSpan? _start;
    private bool _restartPending;

    public void Restart() => _restartPending = true;

    public void Reset()
    {
        _start = null;
        _restartPending = false;
    }

    /// <summary>Starts the pulse at presentationTime if a restart is pending or it has never started; otherwise leaves it running.</summary>
    public void StartPendingAt(TimeSpan presentationTime)
    {
        if (!_restartPending && _start is not null)
        {
            return;
        }

        _start = presentationTime;
        _restartPending = false;
    }

    /// <summary>How long the pulse has run at presentationTime; zero while it waits to start or before its start.</summary>
    public readonly TimeSpan ElapsedAt(TimeSpan presentationTime)
    {
        if (_restartPending || _start is not { } start)
        {
            return TimeSpan.Zero;
        }

        var elapsed = presentationTime - start;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }
}
