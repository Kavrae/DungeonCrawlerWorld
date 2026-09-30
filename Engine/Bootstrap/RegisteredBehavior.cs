using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;

namespace Engine.Bootstrap;

/// <summary>A build whose every module's behavior -- systems and event handlers -- is registered, for the caller's own finishing steps before it completes.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class RegisteredBehavior : EcsBuildStage
{
    internal RegisteredBehavior(EntityManager entityManager, ComponentManager componentManager, SystemManager systemManager, EventBus eventBus)
    {
        EntityManager = entityManager;
        ComponentManager = componentManager;
        SystemManager = systemManager;
        EventBus = eventBus;
    }

    public EntityManager EntityManager { get; }

    public ComponentManager ComponentManager { get; }

    public SystemManager SystemManager { get; }

    public EventBus EventBus { get; }

    public EcsContext Complete()
    {
        MarkAdvanced();
        return new EcsContext(EntityManager, ComponentManager, SystemManager, EventBus);
    }
}
