using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;

namespace Engine.ECS.Context;

/// <summary>Composition root bundling the three ECS managers plus the shared EventBus for a running game.</summary>
public sealed class EcsContext(EntityManager entityManager, ComponentManager componentManager, SystemManager systemManager, EventBus eventBus)
{
    public EntityManager EntityManager { get; } = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
    public ComponentManager ComponentManager { get; } = componentManager ?? throw new ArgumentNullException(nameof(componentManager));
    public SystemManager SystemManager { get; } = systemManager ?? throw new ArgumentNullException(nameof(systemManager));
    public EventBus EventBus { get; } = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

    public void Update(EngineTime time) => SystemManager.Update(time);
}