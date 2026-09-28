using Engine.ECS.Components;
using Engine.ECS.Systems;

namespace Engine.Modules;

/// <summary> A self-contained collection of components and systems for one purpose.</summary>
/// <remarks>
/// Other modules are named by Id, so a mod that replaces one by Id still satisfies everything that names
/// it. Requires is about presence and RunsAfter/RunsBefore about system order, and neither implies the
/// other: every component is registered before any system, so a pool another module owns is available
/// whatever the order. Bootstrapper validates Requires and sorts by the ordering lists, keeping the
/// caller's order wherever nothing constrains it.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface IModule
{
    string Name => GetType().Name;

    /// <summary> Stable identity for replacement</summary>
    /// <remarks>A mod module whose Id matches a built-in module's Id
    /// replaces it instead of being added alongside it. Defaults to Guid.Empty (no identity,
    /// never matches anything) so existing test doubles don't need updating unless they
    /// actually care about replacement. Built-in modules should override this with a real,
    /// literal Guid, the same pattern a race or class definition already uses for identity.
    /// </remarks>
    Guid Id => Guid.Empty;

    /// <summary>Ids of modules that must be in the module set for this one to work.</summary>
    IReadOnlyList<Guid> Requires => [];

    /// <summary>Ids of modules whose systems must run before this one's each frame.</summary>
    /// <remarks>An Id that isn't in the module set is ignored.</remarks>
    IReadOnlyList<Guid> RunsAfter => [];

    /// <summary>Ids of modules whose systems must run after this one's each frame.</summary>
    /// <remarks>An Id that isn't in the module set is ignored.</remarks>
    IReadOnlyList<Guid> RunsBefore => [];

    void RegisterComponents(ComponentManager componentManager);

    void RegisterSystems(SystemManager systemManager, ComponentManager componentManager);
}