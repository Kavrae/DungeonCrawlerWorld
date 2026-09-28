namespace Engine.Modules;

/// <summary>Creates a fresh instance of one module type, so every build gets modules no other build has configured.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed record ModuleFactory<TContext>(Type ModuleType, Func<IModule<TContext>> Create)
{
    public static ModuleFactory<TContext> For<TModule>() where TModule : IModule<TContext>, new() =>
        new(typeof(TModule), static () => new TModule());
}

/// <summary>Instantiates module factories.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class ModuleFactories
{
    /// <summary>A fresh instance from every factory, in order.</summary>
    public static List<IModule<TContext>> CreateAll<TContext>(this IEnumerable<ModuleFactory<TContext>> factories) =>
        factories.Select(factory => factory.Create()).ToList();
}
