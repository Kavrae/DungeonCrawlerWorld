namespace Engine.ECS.Entities;

/// <summary>An entity's permanent identity.</summary>
/// <remarks>
/// Entity ids are smaller for use as component indexes and are recycled as soon as an entity is destroyed to maintain density.
/// Entity keys are larger for long term population growth and are never recycled, so they can be used to identify a specific entity across its lifetime.
/// Default (0) is None, which no entity ever has.
/// </remarks>
public readonly record struct EntityKey(ulong Value)
{
    public static readonly EntityKey None = default;

    public bool IsNone => Value == 0;

    public override string ToString() => IsNone ? "None" : $"Key#{Value}";
}
