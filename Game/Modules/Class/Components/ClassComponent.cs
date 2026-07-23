namespace Game.Modules.Class.Components;

/// <summary>
/// The class details of an entity -- primarily display/narrative properties today, will
/// later include level progression, abilities, and passives. Stored in a MultiComponentPool
/// since an entity can legitimately carry more than one -- e.g. multiclassing.
/// </summary>
public struct ClassComponent(Guid id, string name, string description)
{
    public Guid Id { get; } = id;
    public string Name { get; set; } = name;
    public string Description { get; set; } = description;

    public override readonly string ToString() => $"Class : {Name}";
}