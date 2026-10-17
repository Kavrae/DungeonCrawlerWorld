using Engine.Math;

namespace Game.Modules.Inventory;

/// <summary>Contents that always grant the same items.</summary>
/// <remarks>Equal when their entries are equal in order, so two separately built copies of the same contents stack together.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed record SetItemContents(IReadOnlyList<ItemContentsEntry> Entries) : IItemContents
{
    public IReadOnlyList<ItemContentsEntry> Roll(SeededRandom rolls, ItemCatalog itemCatalog) => Entries;

    public bool Equals(SetItemContents? other) =>
        other is not null && Entries.SequenceEqual(other.Entries);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var entry in Entries)
        {
            hash.Add(entry);
        }

        return hash.ToHashCode();
    }
}
