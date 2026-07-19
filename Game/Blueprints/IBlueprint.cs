using Engine.ECS.Components;

namespace Game.Blueprints;

/// <summary>
/// A pre-defined component list/property set for building game entities. Blueprints are
/// meant to be composed -- a later blueprint in a chain may set a component an earlier one
/// already set, so every blueprint uses <c>Merge</c> rather than <c>Add</c> on component
/// pools, letting a later call combine with rather than throw against an earlier one.
/// Build takes ComponentManager directly rather than resolving it from a locator, so a
/// blueprint's dependencies are visible at the call site.
/// </summary>
public interface IBlueprint
{
    void Build(ComponentManager componentManager, int entityId);
}
