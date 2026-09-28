using Engine.ECS.Components;
using Engine.Settings;

namespace Engine.Modules;

/// <summary>What a module's RegisterComponents phase can reach.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ComponentRegistration(ComponentManager componentManager, SettingValues settings)
{
    public ComponentManager ComponentManager { get; } = componentManager;

    public SettingValues Settings { get; } = settings;
}
