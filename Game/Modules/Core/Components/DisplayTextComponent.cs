namespace Game.Modules.Core.Components;

/// <summary>The name/description shown when a player selects a map node containing this entity.</summary>
public struct DisplayTextComponent(string name, string description)
{
    public string Name { get; set; } = name;
    public string Description { get; set; } = description;

    public override readonly string ToString() => $"{Name} : {Description}";
}