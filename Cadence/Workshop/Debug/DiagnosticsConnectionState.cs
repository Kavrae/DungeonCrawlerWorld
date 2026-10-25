namespace Workshop.Debug;

/// <summary>Whether a game is still connected to the Workshop's diagnostics link.</summary>
public enum DiagnosticsConnectionState
{
    Connected,

    /// <summary>The game closed the link -- it exited, crashed or turned the link off; what it sent stays.</summary>
    Ended,
}
