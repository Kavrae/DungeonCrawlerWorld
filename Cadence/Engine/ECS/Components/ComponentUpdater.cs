namespace Engine.ECS.Components;

/// <summary> Mutates a component already present on an entity, in place. </summary>
/// <cleanupVersion>1</cleanupVersion>
public delegate void ComponentUpdater<T>(ref T component) where T : struct;