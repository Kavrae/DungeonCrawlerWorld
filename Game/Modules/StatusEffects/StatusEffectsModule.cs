using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Modules;
using Game.Modules.StatusEffects.Components;
using Game.Modules.StatusEffects.Systems;

namespace Game.Modules.StatusEffects;

/// <summary>
/// Shared status-effect immunity storage, plus StatusEffectImmunityExpirySystem (the one system
/// every immunity, regardless of which effect it targets, expires through). Individual effects
/// (BurningModule, ...) are their own separate modules depending on this one plus whatever else
/// they specifically need, rather than every effect's system and requirements accumulating into
/// one ever-growing module. Active-effect/stack-count querying (StatusEffectQueries) goes
/// through StatusEffectDisplayRegistry instead -- each effect's own StackCount already lives on
/// its own timer component, so no shared storage is needed for that.
/// </summary>
public sealed class StatusEffectsModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000007");

    public Guid Id => ModuleId;

    private SimulationScope _simulationScope = null!;

    public void Configure(GameModuleContext context) => _simulationScope = context.SimulationScope;

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterMultiPool<StatusEffectImmunityComponent>();
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        systemManager.Register(new StatusEffectImmunityExpirySystem(
            componentManager.GetMultiPool<StatusEffectImmunityComponent>(),
            _simulationScope));
    }
}
