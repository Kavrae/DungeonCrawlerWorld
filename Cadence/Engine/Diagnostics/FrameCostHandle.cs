using System.Diagnostics;

namespace Engine.Diagnostics;

/// <summary>An open frame-cost measurement from EngineHooks.FrameCost; disposing it records the elapsed time.</summary>
/// <remarks>A struct, so `using (EngineHooks.FrameCost(...))` allocates nothing; with no listener it holds nothing and Dispose does nothing.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public readonly struct FrameCostHandle : IDisposable
{
    private readonly IFrameCostRecorder? _recorder;
    private readonly FrameCostCategory _category;
    private readonly string _groupName;
    private readonly string _itemName;
    private readonly long _startTimestamp;

    internal FrameCostHandle(IFrameCostRecorder recorder, FrameCostCategory category, string groupName, string itemName)
    {
        _recorder = recorder;
        _category = category;
        _groupName = groupName;
        _itemName = itemName;
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    public void Dispose() => _recorder?.Record(_category, _groupName, _itemName, Stopwatch.GetElapsedTime(_startTimestamp));
}
