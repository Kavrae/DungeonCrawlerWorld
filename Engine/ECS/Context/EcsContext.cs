using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;

namespace Engine.ECS.World;

/// <summary>Composition root bundling the three ECS managers plus the shared EventBus for a running game.</summary>
public sealed class EcsContext
{
    public EntityManager EntityManager { get; }
    public ComponentManager ComponentManager { get; }
    public SystemManager SystemManager { get; }
    public EventBus EventBus { get; }

    public EcsContext(EntityManager entityManager, ComponentManager componentManager, SystemManager systemManager, EventBus eventBus)
    {
        EntityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        ComponentManager = componentManager ?? throw new ArgumentNullException(nameof(componentManager));
        SystemManager = systemManager ?? throw new ArgumentNullException(nameof(systemManager));
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public void Update(EngineTime time) => SystemManager.Update(time);
}
