namespace Engine.ECS.Components;

/// <summary>
/// Combines a newly-added component into one an entity already has (e.g. when a chained
/// blueprint adds the same component type twice). See <see cref="ComponentManager"/>.
/// </summary>
public delegate void MergeAction<T>(ref T existingComponent, T newComponent) where T : struct;
