using Engine.Settings;
using Engine.Tags;

namespace Engine.Modules;

/// <summary>A self-contained collection of components and systems for one purpose: its identity, its ordering, and the phases that need no context.</summary>
/// <remarks>
/// Other modules are named by Id, so a mod that replaces one by Id still satisfies everything that names
/// it. Requires is about presence and RunsAfter/RunsBefore about system order, and neither implies the
/// other: every component is registered before any module is configured or any behavior registered, so a
/// pool another module owns is available whatever the order. The phases run in this order, each for
/// every module before the next starts: DeclareSettings and DeclareTags, RegisterComponents, then
/// <see cref="IModule{TContext}"/>'s Configure and RegisterBehavior.
///
/// Implement <see cref="IModule{TContext}"/>, never this alone: the builder, the loader and
/// replacement all work on the generic form. This non-generic base is what identity, ordering and
/// replacement read.
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

    /// <summary>Declares every setting this module reads, with its default.</summary>
    /// <remarks>Runs before any other phase, with no pools yet. A module that declares settings must have a non-empty Id.</remarks>
    void DeclareSettings(SettingsDeclarations settings)
    {
    }

    /// <summary>Declares every gameplay tag this module's content uses, with any display names.</summary>
    /// <remarks>Runs with DeclareSettings, before any other phase, with no pools yet.</remarks>
    void DeclareTags(GameplayTagDeclarations tags)
    {
    }

    void RegisterComponents(ComponentRegistration registration);
}

/// <summary>A module configured with, and registering its behavior against, a context of type TContext.</summary>
/// <remarks>The context is whatever the layer building the modules shares between them; Engine never looks inside it.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface IModule<TContext> : IModule
{
    /// <summary>Fills whatever the context shares that another module's RegisterBehavior reads.</summary>
    /// <remarks>Runs after every module's RegisterComponents and before any module's RegisterBehavior.</remarks>
    void Configure(TContext context)
    {
    }

    /// <summary>Wires everything this module runs: its systems, its event handlers, and any setup that decides which systems exist.</summary>
    /// <remarks>
    /// Runs after every module's Configure, so every catalog and registry the context shares is complete. Modules run it in
    /// sorted order, which is both the order their systems run each frame and the order their handlers receive an event.
    /// </remarks>
    void RegisterBehavior(BehaviorRegistration<TContext> registration);
}
