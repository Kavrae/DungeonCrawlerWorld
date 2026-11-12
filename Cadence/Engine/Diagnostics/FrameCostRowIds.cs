namespace Engine.Diagnostics;

/// <summary>Issues each frame-cost row name one process-wide FrameCostRowId, and names an id back.</summary>
/// <remarks>
/// Ids are dense, from 0, and never reused, so a recorder keeps its totals in arrays indexed by id. Issuing takes a lock,
/// so an emit site asks once -- when it is created (a system's registration, an event type's first measurement, a static
/// field) -- and never per measurement. Every recorder shares the ids, so two recorders never disagree about a row.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class FrameCostRowIds
{
    private static readonly Lock IssueLock = new();
    private static readonly Dictionary<FrameCostRowName, FrameCostRowId> IdsByName = [];
    private static FrameCostRowName[] _namesById = new FrameCostRowName[64];
    private static int _issuedCount;

    /// <summary>How many ids have been issued; every id's Index is below it.</summary>
    public static int IssuedCount => Volatile.Read(ref _issuedCount);

    /// <summary>The id of the row with this name, issuing it the first time the name is asked for.</summary>
    public static FrameCostRowId Of(FrameCostCategory category, string groupName, string itemName)
    {
        var name = new FrameCostRowName(category, groupName, itemName);
        lock (IssueLock)
        {
            if (IdsByName.TryGetValue(name, out var existingId))
            {
                return existingId;
            }

            var id = new FrameCostRowId(_issuedCount);
            if (id.Index == _namesById.Length)
            {
                var grownNames = new FrameCostRowName[_namesById.Length * 2];
                _namesById.CopyTo(grownNames, 0);
                Volatile.Write(ref _namesById, grownNames);
            }

            _namesById[id.Index] = name;
            IdsByName.Add(name, id);
            Volatile.Write(ref _issuedCount, id.Index + 1);
            return id;
        }
    }

    /// <summary>The id already issued for this name, if any; issues none.</summary>
    public static bool TryFind(FrameCostCategory category, string groupName, string itemName, out FrameCostRowId id)
    {
        lock (IssueLock)
        {
            return IdsByName.TryGetValue(new FrameCostRowName(category, groupName, itemName), out id);
        }
    }

    public static FrameCostRowName NameOf(FrameCostRowId id) => Volatile.Read(ref _namesById)[id.Index];
}
