namespace Engine.ECS.Components;

/// <summary>Represents an entry for inspecting a component.</summary>
/// <param name="ComponentType">The type of the component.</param>
/// <param name="Value">The pre-formatted string representing the component's value.</param>
/// <param name="Version">The update version of the component.</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct InspectedComponentEntry(
    Type ComponentType,
    string Value,
    uint Version
);