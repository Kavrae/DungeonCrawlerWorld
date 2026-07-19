namespace Engine.ECS.Components;

public readonly record struct InspectedComponentEntry(
    Type ComponentType,
    ComponentPoolType ComponentPoolType,
    object Value,
    uint Version
);
