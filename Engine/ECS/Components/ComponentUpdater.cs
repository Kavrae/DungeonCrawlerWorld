namespace Engine.ECS.Components;

/// <summary>
/// Mutates a component already present on an entity, in place. See <see cref="MergeAction{T}"/>
/// for the equivalent "combine with an incoming value" delegate used by Merge.
/// </summary>
public delegate void ComponentUpdater<T>(ref T component) where T : struct;