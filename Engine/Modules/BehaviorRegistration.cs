using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Settings;

namespace Engine.Modules;

/// <summary>What a module's RegisterBehavior phase can reach, including the context every module was configured with.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class BehaviorRegistration<TContext>(SystemManager systemManager, ComponentManager componentManager, SettingValues settings, TContext context)
{
    public SystemManager SystemManager { get; } = systemManager;

    public ComponentManager ComponentManager { get; } = componentManager;

    public SettingValues Settings { get; } = settings;

    public TContext Context { get; } = context;
}
