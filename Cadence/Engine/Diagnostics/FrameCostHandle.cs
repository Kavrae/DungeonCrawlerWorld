using System.Diagnostics;

namespace Engine.Diagnostics;

/// <summary>An open frame-cost measurement from EngineHooks.FrameCost; disposing it records the elapsed time.</summary>
/// <remarks>A struct, so `using (EngineHooks.FrameCost(...))` allocates nothing; with no listener it holds nothing and Dispose does nothing.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public readonly struct FrameCostHandle : IDisposable
{
    private readonly IFrameCostRecorder? _recorder;
    private readonly FrameCostRowId _row;
    private readonly long _startTimestamp;

    internal FrameCostHandle(IFrameCostRecorder recorder, FrameCostRowId row)
    {
        _recorder = recorder;
        _row = row;
        recorder.Begin(row);
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    public void Dispose() => _recorder?.Record(_row, Stopwatch.GetElapsedTime(_startTimestamp));
}
