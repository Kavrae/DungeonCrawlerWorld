namespace Engine.ECS.Components;

/// <summary>The dense-storage capacity a Packed or Multi pool grows to when it fills.</summary>
/// <remarks>
/// Geometric (x1.5), so filling a pool to N copies about 3N elements in total however small it
/// started, where a fixed step copies O(N²/step). 1.5 rather than 2 keeps the unused tail after the
/// last growth under half the count.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
internal static class DenseCapacityGrowth
{
    private const int MinimumGrowth = 16;

    public static int Next(int currentCapacity) =>
        (int)System.Math.Min(Array.MaxLength, currentCapacity + System.Math.Max(MinimumGrowth, (long)currentCapacity / 2));
}
