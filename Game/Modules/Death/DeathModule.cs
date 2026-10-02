using Engine.Modules;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Death.Systems;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;

namespace Game.Modules.Death;

/// <summary>Marks entities dead and clears their map occupancy, through the mandatory IEntityMoveSync MovementModule also uses.</summary>
public sealed class DeathModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000015");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [CoreModule.ModuleId, AurasModule.ModuleId];

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterPackedPool<DeadComponent>(static (ref existing, incoming) => existing = incoming);

        // Rare in practice (player-action-only, seconds to minutes between uses), so initialCapacity is reduced.
        componentManager.RegisterPackedPool<LootedComponent>(static (ref existing, incoming) => existing = incoming, initialCapacity: 32);
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var systemManager = registration.SystemManager;
        var componentManager = registration.ComponentManager;

        var auraSources = componentManager.GetMultiPool<AuraSourceComponent>();

        systemManager.Register(new DeathSystem(
            componentManager.GetPackedPool<DeadComponent>(),
            componentManager.GetMultiPool<NonBlockingComponent>(),
            componentManager.GetDirectPool<TransformComponent>(),
            context.EntityMoveSync,
            context.MapQuery,
            context.EventBus,
            auraSources));
    }
}
