using Engine.ECS.Components;
using Engine.ECS.Systems;
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
public sealed class StatModifiersModule : IModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000012");

    public void RegisterComponents(ComponentManager componentManager) =>
        componentManager.RegisterMultiPool<StatModifierComponent>();

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager) =>
        systemManager.Register(new StatModifierExpirySystem(componentManager.GetMultiPool<StatModifierComponent>()));
}
