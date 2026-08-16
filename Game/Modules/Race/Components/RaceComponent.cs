namespace Game.Modules.Race.Components;

/// <summary> The racial details of an entity </summary>
/// <cleanupVersion>1</cleanupVersion>
public struct RaceComponent(Guid id, string name, string description)
{
    public Guid Id { get; } = id;
    public string Name { get; set; } = name;
    public string Description { get; set; } = description;

    public override readonly string ToString() => $"Id : {Id}\nName : {Name}\nDescription : {Description}";
}