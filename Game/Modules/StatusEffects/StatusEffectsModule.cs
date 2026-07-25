using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Modules;
using Game.Modules.StatusEffects.Components;

namespace Game.Modules.StatusEffects;

/// <summary>
/// Shared status-effect storage only -- no systems of its own, the same shape as CoreModule.
/// Individual effects (BurningModule, ...) are their own separate modules depending on this
/// one plus whatever else they specifically need, rather than every effect's system and
/// Dependencies accumulating into one ever-growing module.
/// </summary>
public sealed class StatusEffectsModule : IModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000007");

    public void RegisterComponents(ComponentManager componentManager) =>
        componentManager.RegisterMultiPool<StatusEffectStack>();

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        // No systems of its own -- shared storage only, effect modules build on it.
    }
}
