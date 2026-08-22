namespace Engine.ECS.Components;

/// <summary>Shared doubling-growth formula for anything that needs to grow an entity-indexed array to cover a specific entity id.</summary>
internal static class EntityCapacityGrowth
{
    /// <summary>Doubles currentCapacity, or jumps straight to entityId + 1 if doubling still wouldn't cover it.</summary>
    public static int NextCapacityFor(int currentCapacity, int entityId)
    {
        var newCapacity = currentCapacity * 2;
        if (newCapacity <= entityId)
        {
            newCapacity = entityId + 1;
        }

        return newCapacity;
    }
}
