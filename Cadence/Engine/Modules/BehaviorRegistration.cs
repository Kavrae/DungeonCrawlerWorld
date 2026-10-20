using Engine.Content;
using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Settings;
using Engine.Tags;

namespace Engine.Modules;

/// <summary>What a module's RegisterBehavior phase can reach, including the context every module was configured with.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class BehaviorRegistration<TContext>(SystemManager systemManager, ComponentManager componentManager, EventBus eventBus, ModuleDeclarations declarations, TContext context)
{
    public SystemManager SystemManager { get; } = systemManager;

    public ComponentManager ComponentManager { get; } = componentManager;

    public EventBus EventBus { get; } = eventBus;

    /// <summary>Everything the build's modules declared.</summary>
    public ModuleDeclarations Declarations { get; } = declarations;

    public TContext Context { get; } = context;

    /// <summary>The simulation's "now", advanced by SystemManager -- the same clock the context was given.</summary>
    public SimulationClock Clock => SystemManager.Clock;

    public EntityKeys EntityKeys => ComponentManager.Keys;

    public SettingValues Settings => Declarations.Settings;

    public GameplayTagRegistry Tags => Declarations.Tags;

    public ContentTypeRegistry ContentTypes => Declarations.ContentTypes;
}
