using Engine.ECS.Components;

namespace Game.Blueprints;

/// <summary>
/// Composes an ordered list of blueprint parts onto one entity, then applies an optional
/// final overrides step. Generalizes the "race + class, then overwrite specific properties"
/// pattern (see GoblinEngineerBlueprint) to any number/kind of parts -- e.g. race + race +
/// magic system + ghost, or a collection of components with no race/class at all, like a
/// wall with magical properties and an attack. Parts run in list order, so a part that
/// TryUpdates a component set by an earlier part (e.g. a class boosting a race's energy)
/// must be listed after it.
/// </summary>
public sealed class CompositeBlueprint(IReadOnlyList<IBlueprint> parts, Action<ComponentManager, int>? overrides = null) : IBlueprint
{
    public void Build(ComponentManager componentManager, int entityId)
    {
        foreach (var part in parts)
        {
            part.Build(componentManager, entityId);
        }

        overrides?.Invoke(componentManager, entityId);
    }
}
