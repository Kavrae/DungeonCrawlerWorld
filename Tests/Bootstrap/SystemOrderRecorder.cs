using Engine.Diagnostics;

namespace Tests.Bootstrap;

/// <summary>Records the name of every system SystemManager runs, in the order it runs them.</summary>
internal sealed class SystemOrderRecorder : IFrameCostRecorder
{
    public List<string> SystemNames { get; } = [];

    public void Record(FrameCostRowId row, TimeSpan elapsed)
    {
        if (row.Name.GroupName == "SystemManager")
        {
            SystemNames.Add(row.Name.ItemName);
        }
    }
}
