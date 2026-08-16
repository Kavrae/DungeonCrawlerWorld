namespace Engine.ECS.Components;

/// <summary>Represents an action that merges a new component into an existing one.</summary>
/// <typeparam name="T">The type of the component.</typeparam>
/// <param name="existingComponent">A reference to the existing component.</param>
/// <param name="newComponent">The new component to merge.</param>
/// <cleanupVersion>1</cleanupVersion>
public delegate void MergeAction<T>(ref T existingComponent, T newComponent) where T : struct;