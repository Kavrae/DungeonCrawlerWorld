using Game.Modules.Actions;

namespace Tests.Determinism;

/// <summary>What a trace entry records.</summary>
internal enum SimulationTraceKind : byte
{
    /// <summary>A creature acted on the behavior named by Id.</summary>
    Decision,

    /// <summary>An action, by its definition's Guid, took effect.</summary>
    ActionUsed,

    /// <summary>An item, by its definition's Guid, took effect.</summary>
    ItemUsed,
}

/// <summary>One thing that happened: when, who by its key, and what by its Guid.</summary>
internal readonly record struct SimulationTraceEntry(long Frame, ulong EntityKey, SimulationTraceKind Kind, Guid Id);

/// <summary>What a simulation did, in the order it did it, in identifiers that mean the same in every session.</summary>
/// <remarks>Entity keys and definition Guids only: entity ids are recycled, and interned handles and stack ids are process-wide counters that differ between two sessions in one process.</remarks>
internal sealed class SimulationTrace
{
    private readonly List<SimulationTraceEntry> _entries = [];

    public IReadOnlyList<SimulationTraceEntry> Entries => _entries;

    public void RecordDecision(long frame, ulong entityKey, Guid behaviorId) =>
        _entries.Add(new SimulationTraceEntry(frame, entityKey, SimulationTraceKind.Decision, behaviorId));

    public void RecordActivation(long frame, ulong entityKey, ActivatableKind kind, Guid definitionId) =>
        _entries.Add(new SimulationTraceEntry(frame, entityKey, kind == ActivatableKind.Action ? SimulationTraceKind.ActionUsed : SimulationTraceKind.ItemUsed, definitionId));

    /// <summary>Fails naming the first entry where the two traces differ.</summary>
    public static void AssertSame(SimulationTrace expected, SimulationTrace actual)
    {
        var shared = System.Math.Min(expected._entries.Count, actual._entries.Count);
        for (var index = 0; index < shared; index++)
        {
            Assert.AreEqual(expected._entries[index], actual._entries[index], $"The traces first differ at entry {index}.");
        }

        Assert.HasCount(expected._entries.Count, actual._entries, "One trace ends before the other.");
    }
}
