using Engine.Diagnostics;

namespace Tests.Bootstrap;

/// <summary>Records the name of every system SystemManager runs, in the order it runs them.</summary>
internal sealed class SystemOrderRecorder : IFrameCostRecorder
{
    public List<string> SystemNames { get; } = [];

    public void Record(FrameCostCategory category, string groupName, string itemName, TimeSpan elapsed)
    {
        if (groupName == "SystemManager")
        {
            SystemNames.Add(itemName);
        }
    }
}
