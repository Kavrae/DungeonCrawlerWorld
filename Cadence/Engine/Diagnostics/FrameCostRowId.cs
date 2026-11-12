namespace Engine.Diagnostics;

/// <summary>A frame-cost row's process-wide number, issued once per name by FrameCostRowIds.</summary>
/// <remarks>An emit site holds its row's id from when it is created, so recording a measurement indexes arrays and never looks a name up.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct FrameCostRowId
{
    internal FrameCostRowId(int index) => Index = index;

    /// <summary>The row's place in every recorder's per-row arrays.</summary>
    public int Index { get; }

    public FrameCostRowName Name => FrameCostRowIds.NameOf(this);

    public override string ToString() => $"{Name.GroupName}.{Name.ItemName}";
}
