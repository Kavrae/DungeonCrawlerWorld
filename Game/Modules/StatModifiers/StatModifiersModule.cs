using Engine.Modules;
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
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000001b");

    public Guid Id => ModuleId;

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterMultiPool<StatModifierComponent>();

        // Merging keeps the EARLIER deadline: this is an entity's "next modifier to expire", so a
        // newly granted modifier only ever pulls it forward, and one granted with a later deadline
        // leaves the pending firing alone (StatModifierExpirySystem re-arms to it in due course).
        componentManager.RegisterPackedPool<ExpiringStatModifierComponent>(static (ref existing, incoming) =>
            existing.NextTickFrame = System.Math.Min(existing.NextTickFrame, incoming.NextTickFrame));
    }

    public void RegisterSystems(SystemRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var systemManager = registration.SystemManager;
        var componentManager = registration.ComponentManager;

        systemManager.Register(new StatModifierExpirySystem(
            componentManager.GetMultiPool<StatModifierComponent>(),
            componentManager.GetPackedPool<ExpiringStatModifierComponent>(),
            context.EventBus));
    }
}
