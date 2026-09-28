using Engine.ECS.Components;
using Engine.Modules;
using Engine.Settings;

namespace Tests;

/// <summary>Runs one module's RegisterComponents on a test's own ComponentManager, with that module's default settings.</summary>
internal static class TestModuleRegistration
{
    public static void RegisterComponents(this IModule module, ComponentManager componentManager) =>
        module.RegisterComponents(new ComponentRegistration(componentManager, DefaultSettings([module])));

    /// <summary>Every setting modules declare, at its default.</summary>
    public static SettingValues DefaultSettings(IEnumerable<IModule> modules) =>
        SettingsCatalog.Declare(modules).Resolve([]).Values;
}
