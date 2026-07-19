namespace Game.Modules.Race.Components;

/// <summary>
/// The racial details of an entity -- primarily display/narrative properties today, but
/// frequently consulted by future systems. Stored in a MultiComponentPool (not Direct or
/// Packed) since an entity can legitimately carry more than one -- e.g. a race change
/// keeping the prior race on record rather than overwriting it.
/// </summary>
public struct RaceComponent(Guid id, string name, string description)
{
    public Guid Id { get; } = id;
    public string Name { get; set; } = name;
    public string Description { get; set; } = description;

    public override readonly string ToString() => $"Race : {Name}";
}
