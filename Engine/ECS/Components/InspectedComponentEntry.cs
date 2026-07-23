namespace Engine.ECS.Components;

/// <summary>
/// Value is a pre-formatted string, not the raw component -- every current caller
/// (SelectionWindowContent) only ever displays it, and capturing it as T.ToString() at the
/// pool (T : struct) dispatches through a constrained virtual call rather than boxing the
/// component into an object first.
/// </summary>
public readonly record struct InspectedComponentEntry(
    Type ComponentType,
    ComponentPoolType ComponentPoolType,
    string Value,
    uint Version
);