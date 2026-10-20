using Engine.ECS.Components;
using Engine.ECS.Relationships;
using Engine.Modules;
using Engine.Settings;

namespace Engine.Bootstrap;

/// <summary>A mod module that replaces one of the game's modules by Id must register every component type the replaced module did, each as the same kind of pool.</summary>
/// <remarks>
/// What other modules fetch with Get*Pool must still be there, as the same kind, whichever module registered it. Each
/// side registers alone on a scratch ComponentManager: the replacement with the build's declarations, the replaced
/// module with its own settings as the build's sources resolve them, since its DeclareSettings never ran in the build.
/// A build with no replacement does nothing here.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
internal static class ReplacementContract
{
    /// <summary>Each of the game's modules a mod replaces, with the mod that replaces it -- the last mod sharing its Id, as ModuleSet.Combine decides.</summary>
    public static List<(IModule<TContext> Replaced, IModule<TContext> Replacement)> FindReplacements<TContext>(IReadOnlyList<IModule<TContext>> gameModules, IReadOnlyList<IModule<TContext>> mods)
    {
        var replacements = new List<(IModule<TContext> Replaced, IModule<TContext> Replacement)>();

        foreach (var gameModule in gameModules)
        {
            if (gameModule.Id == Guid.Empty)
            {
                continue;
            }

            var replacement = mods.LastOrDefault(mod => mod.Id == gameModule.Id);
            if (replacement is not null)
            {
                replacements.Add((gameModule, replacement));
            }
        }

        return replacements;
    }

    /// <exception cref="InvalidOperationException">A replacement doesn't register a component type the module it replaces did, as the same kind of pool; the message names each one.</exception>
    public static void ThrowIfBroken<TContext>(List<(IModule<TContext> Replaced, IModule<TContext> Replacement)> replacements, ModuleDeclarations buildDeclarations, IReadOnlyList<ISettingsSource> settingsSources)
    {
        foreach (var (replaced, replacement) in replacements)
        {
            var providedPools = RegisteredPools(replacement, buildDeclarations);
            var replacedDeclarations = buildDeclarations with { Settings = SettingsCatalog.Declare([replaced]).Resolve(settingsSources).Values };
            var missingPools = RegisteredPools(replaced, replacedDeclarations)
                .Where(requiredPool => !providedPools.Contains(requiredPool))
                .Select(missingPool => $"{missingPool.ComponentType.Name} ({missingPool.PoolKind.Name[..missingPool.PoolKind.Name.IndexOf('`')]})")
                .ToList();

            if (missingPools.Count > 0)
            {
                throw new InvalidOperationException($"{replacement.Name} replaces {replaced.Name} but does not register: {string.Join(", ", missingPools)}.");
            }
        }
    }

    /// <summary>Each component type module registers, with the kind of pool it registers it as (DirectComponentPool&lt;&gt; and so on, or Relationship&lt;&gt; for either side of one).</summary>
    private static HashSet<(Type ComponentType, Type PoolKind)> RegisteredPools(IModule module, ModuleDeclarations declarations)
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 1, initialComponentCapacity: 1);
        module.RegisterComponents(new ComponentRegistration(componentManager, declarations));

        return componentManager.PoolsInRegistrationOrder
            .Select(pool => (pool.ComponentType, componentManager.Relationships.IsRelationshipComponentType(pool.ComponentType) ? typeof(Relationship<>) : pool.GetType().GetGenericTypeDefinition()))
            .ToHashSet();
    }
}
