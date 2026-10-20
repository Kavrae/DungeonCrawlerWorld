using Engine.ECS.Components;
using Engine.Modules;

namespace Tests;

/// <summary>Runs one module's RegisterComponents on a test's own ComponentManager, with that module's own declarations and default settings.</summary>
internal static class TestModuleRegistration
{
    public static void RegisterComponents(this IModule module, ComponentManager componentManager) =>
        module.RegisterComponents(new ComponentRegistration(componentManager, ModuleDeclarations.Declare([module], [])));
}
