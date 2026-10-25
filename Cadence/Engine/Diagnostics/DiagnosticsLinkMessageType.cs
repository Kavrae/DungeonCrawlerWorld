namespace Engine.Diagnostics;

/// <summary>What a message on the diagnostics link carries -- the type byte after each message's length.</summary>
/// <remarks>Values are the protocol: append, never renumber. A reader skips a type it doesn't know.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public enum DiagnosticsLinkMessageType : byte
{
    /// <summary>The game's first message once connected, JSON: DiagnosticsLinkHello.</summary>
    Hello = 1,

    /// <summary>A simulated session began, JSON: DiagnosticsLinkSessionStart, its gauges in the order every frame record lists their values.</summary>
    SessionStarted = 2,

    /// <summary>One frame record, binary: see DiagnosticsFrameRecord.</summary>
    Frame = 3,

    /// <summary>The periodic report, the same JSON latest.json holds.</summary>
    PeriodicReport = 4,

    /// <summary>The startup report once frame pacing settled, the same JSON the startup file holds.</summary>
    StartupReport = 5,

    /// <summary>A benchmark range closed, JSON: DiagnosticsLinkBenchmarkCompletion.</summary>
    BenchmarkCompleted = 6,

    /// <summary>The simulated session ended; no payload.</summary>
    SessionEnded = 7,
}
