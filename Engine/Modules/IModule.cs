using Engine.ECS.Components;
using Engine.ECS.Systems;

namespace Engine.Modules;

/// <summary>
/// A self-contained collection of components and systems for one purpose (e.g. Movement,
/// Health). Dependencies lists other modules this one requires (e.g. Movement requires
/// Core for TransformComponent) -- Bootstrapper validates and topologically sorts these
/// before registration, rather than relying on hand-ordered registration calls.
/// </summary>
public interface IModule
{
    string Name => GetType().Name;

    /// <summary>
    /// Stable identity for replacement: a mod module whose Id matches a built-in module's Id
    /// replaces it instead of being added alongside it. Defaults to Guid.Empty (no identity,
    /// never matches anything) so existing test doubles don't need updating unless they
    /// actually care about replacement. Built-in modules should override this with a real,
    /// literal Guid, the same pattern RaceComponent/ClassComponent already use for identity.
    /// </summary>
    Guid Id => Guid.Empty;

    IReadOnlyList<Type> Dependencies => [];

    void RegisterComponents(ComponentManager componentManager);

    void RegisterSystems(SystemManager systemManager, ComponentManager componentManager);
}
