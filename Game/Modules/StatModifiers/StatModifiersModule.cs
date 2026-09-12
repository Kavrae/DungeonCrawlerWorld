using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatModifiers.Systems;

namespace Game.Modules.StatModifiers;

/// <summary>
/// Shared active-modifier storage plus the system that expires them -- the same shape as
/// StatusEffectsModule (shared storage) combined with a Poison/Burning-style effect module
/// (its own timer/expiry system), since unlike status effects, stat modifiers don't split into
/// several separate per-effect modules -- there's one generic modifier record, not one type per
/// effect.
/// </summary>
public sealed class StatModifiersModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000012");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    private EventBus _eventBus = null!;

    public void Configure(GameModuleContext context)
    {
        _eventBus = context.EventBus;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterMultiPool<StatModifierComponent>();

        // Merging keeps the EARLIER deadline: this is an entity's "next modifier to expire", so a
        // newly granted modifier only ever pulls it forward, and one granted with a later deadline
        // leaves the pending firing alone (StatModifierExpirySystem re-arms to it in due course).
        componentManager.RegisterPackedPool<ExpiringStatModifierComponent>(static (ref existing, incoming) =>
            existing.NextTickFrame = System.Math.Min(existing.NextTickFrame, incoming.NextTickFrame));
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager) =>
        systemManager.Register(new StatModifierExpirySystem(
            componentManager.GetMultiPool<StatModifierComponent>(),
            componentManager.GetPackedPool<ExpiringStatModifierComponent>(),
            _eventBus));
}
