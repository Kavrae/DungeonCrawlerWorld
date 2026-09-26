namespace Game.Spawning;

/// <summary>Facts about an entity that belong to its spawn, not to its blueprint.</summary>
/// <cleanupVersion>1</cleanupVersion>
[Flags]
public enum SpawnFlags : byte
{
    None = 0,

    /// <summary>A Crawler: given a CrawlerComponent with the session's next crawler number when it is first built.</summary>
    Crawler = 1,
}

/// <summary>What an entity was spawned as: its blueprint, the seed every random choice its build made was drawn from, and the spawn's own flags.</summary>
/// <remarks>Enough for EntityFactory to rebuild the entity's blueprint-built components exactly (see SpawnRecordRebuilder). Written once at spawn and never changed. 8 bytes in a Direct pool over every entity id: Flags sits in what would otherwise be padding between BlueprintId and Seed, so keep the fields in this order.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct SpawnRecordComponent(ushort BlueprintId, SpawnFlags Flags, uint Seed)
{
    /// <summary>A record with no spawn flags.</summary>
    public SpawnRecordComponent(ushort blueprintId, uint seed)
        : this(blueprintId, SpawnFlags.None, seed)
    {
    }

    public override string ToString() => $"Blueprint : {BlueprintId}\nFlags : {Flags}\nSeed : {Seed}";
}
