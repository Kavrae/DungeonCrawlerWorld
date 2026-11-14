namespace Engine.Decisions;

/// <summary>How a decision reached its choice.</summary>
public enum DecisionPath : byte
{
    /// <summary>Nothing applied.</summary>
    Nothing,

    /// <summary>Chosen among every behavior in the set.</summary>
    FreshPick,

    /// <summary>A behavior ranked above a live commitment won before the commitment was asked.</summary>
    Interrupt,

    /// <summary>The live commitment still applied and was kept.</summary>
    Continued,
}

/// <summary>What became of one evaluated behavior in a decision.</summary>
public enum DecisionTraceOutcome : byte
{
    /// <summary>It didn't apply: rank 0, no weight, or not above what it had to beat.</summary>
    DidNotApply,

    /// <summary>It applied, but a consideration or the runtime multiplier brought its weight to 0.</summary>
    Vetoed,

    /// <summary>It applied, but a behavior at a higher rank applied too.</summary>
    Outranked,

    /// <summary>It was at the winning rank, but under the cutoff's share of the best weight there.</summary>
    BelowCutoff,

    /// <summary>It was in the final draw and wasn't drawn.</summary>
    NotDrawn,

    /// <summary>It was chosen.</summary>
    Chosen,
}

/// <summary>One evaluated behavior in a traced decision: every factor of its weight, and what became of it.</summary>
/// <param name="EntryIndex">Its place in the set.</param>
/// <param name="IsCommitted">Whether this is the live commitment being asked to continue.</param>
public readonly record struct DecisionTraceEntry(
    int EntryIndex,
    string BehaviorName,
    byte RankCeiling,
    byte Rank,
    float IntrinsicWeight,
    float ResolvedWeight,
    float ConsiderationsProduct,
    float RuntimeMultiplier,
    float FinalWeight,
    bool IsCommitted,
    DecisionTraceOutcome Outcome);

/// <summary>One decision of one entity, layer by layer: every behavior the arbiter evaluated and why it won or lost.</summary>
/// <remarks>Optional and reused: a caller passes one only for the entity it inspects, and the arbiter clears it at the start of each Choose. Normal play passes none.</remarks>
public sealed class DecisionTrace
{
    private readonly List<DecisionTraceEntry> _entries = [];

    public long Frame { get; private set; }

    public DecisionPath Path { get; internal set; }

    /// <summary>In the order they were evaluated.</summary>
    public IReadOnlyList<DecisionTraceEntry> Entries => _entries;

    internal void Begin(long frame)
    {
        Frame = frame;
        Path = DecisionPath.Nothing;
        _entries.Clear();
    }

    internal void Add(DecisionTraceEntry entry) => _entries.Add(entry);

    /// <summary>Drops the first count entries: an interrupt pass's, when the fresh pick that follows lists the same behaviors again.</summary>
    internal void RemoveFirst(int count) => _entries.RemoveRange(0, count);

    internal int Count => _entries.Count;

    internal DecisionTraceEntry this[int index]
    {
        get => _entries[index];
        set => _entries[index] = value;
    }
}
