using System.Runtime.CompilerServices;

namespace Engine.Diagnostics;

/// <summary>One frame-cost row's name: its category, its group (the recorder) and its item (the system, event or window).</summary>
/// <cleanupVersion>1</cleanupVersion>
internal readonly record struct FrameCostRowName(FrameCostCategory Category, string GroupName, string ItemName);

/// <summary>A frame-cost recorder's rows, each holding its totals, found by name with no string hashing on the common path.</summary>
/// <remarks>
/// Emit sites pass the same string instances every time (constants, a type's cached Name, an enum's cached name), so a row
/// is first looked up by the names' references, which hashes two object addresses. A name seen for the first time as a
/// new instance is then looked up by value, so a site that builds its strings still lands on one row per name. Rows are
/// never removed: a recorder that restarts its window zeroes their totals.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
internal sealed class FrameCostRows<TTotals> where TTotals : struct
{
    /// <summary>How many name instances are remembered by reference; past it, a site that builds a new string per call is looked up by value rather than growing the table.</summary>
    private const int MaximumNameReferences = 4096;

    private readonly Dictionary<FrameCostRowName, int> _rowIndexByNameValue = [];
    private readonly Dictionary<FrameCostRowName, int> _rowIndexByNameReference = new(NameReferenceComparer.Instance);
    private readonly List<FrameCostRowName> _rowNames = [];
    private TTotals[] _rowTotals = new TTotals[16];

    public int Count => _rowNames.Count;

    public FrameCostRowName NameAt(int rowIndex) => _rowNames[rowIndex];

    public ref TTotals TotalsAt(int rowIndex) => ref _rowTotals[rowIndex];

    /// <summary>The totals of the row with this name, adding an empty row the first time the name is seen.</summary>
    public ref TTotals GetOrAdd(FrameCostCategory category, string groupName, string itemName)
    {
        var name = new FrameCostRowName(category, groupName, itemName);
        if (_rowIndexByNameReference.TryGetValue(name, out var rowIndex))
        {
            return ref _rowTotals[rowIndex];
        }

        if (!_rowIndexByNameValue.TryGetValue(name, out rowIndex))
        {
            rowIndex = _rowNames.Count;
            _rowNames.Add(name);
            _rowIndexByNameValue.Add(name, rowIndex);
            if (rowIndex == _rowTotals.Length)
            {
                Array.Resize(ref _rowTotals, _rowTotals.Length * 2);
            }
        }

        if (_rowIndexByNameReference.Count < MaximumNameReferences)
        {
            _rowIndexByNameReference.Add(name, rowIndex);
        }

        return ref _rowTotals[rowIndex];
    }

    /// <summary>The row with this name, compared by value, if it was ever added.</summary>
    public bool TryGetRowIndex(FrameCostCategory category, string groupName, string itemName, out int rowIndex) =>
        _rowIndexByNameValue.TryGetValue(new FrameCostRowName(category, groupName, itemName), out rowIndex);

    /// <summary>Zeroes every row's totals, keeping the rows.</summary>
    public void ClearTotals() => Array.Clear(_rowTotals, 0, _rowNames.Count);

    private sealed class NameReferenceComparer : IEqualityComparer<FrameCostRowName>
    {
        public static readonly NameReferenceComparer Instance = new();

        public bool Equals(FrameCostRowName first, FrameCostRowName second) =>
            first.Category == second.Category && ReferenceEquals(first.GroupName, second.GroupName) && ReferenceEquals(first.ItemName, second.ItemName);

        public int GetHashCode(FrameCostRowName name) =>
            HashCode.Combine(name.Category, RuntimeHelpers.GetHashCode(name.GroupName), RuntimeHelpers.GetHashCode(name.ItemName));
    }
}
