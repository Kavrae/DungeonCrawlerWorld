using Engine.ECS.Components;
using Engine.ECS.Systems;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Death.Components;
using Game.Modules.Mana.Components;
using Game.Modules.Mana.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;
using Microsoft.Xna.Framework;
using Game.Modules.AbilityScores;
using Game.Modules.Death;
using Game.Modules.StatModifiers;

namespace Game.Modules.Mana;

public sealed class ManaModule : IGameModule
{
    public static readonly Guid ModuleId = new("8e2a4f61-3c9d-4b7e-a1f5-6d8c2b9e4a71");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [StatModifiersModule.ModuleId, DeathModule.ModuleId, AbilityScoresModule.ModuleId, ProcessingTierModule.ModuleId];

    private ProcessingTierEvents _processingTierEvents = null!;

    public void Configure(GameModuleContext context) => _processingTierEvents = context.ProcessingTierEvents;

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<ManaComponent>(static (ref existing, incoming) =>
        {
            existing.MaximumMana = MathHelper.Clamp((existing.MaximumMana + incoming.MaximumMana) / 2f, 0f, float.MaxValue);
            existing.CurrentMana = MathHelper.Clamp((existing.CurrentMana + incoming.CurrentMana) / 2f, 0f, existing.MaximumMana);
        });
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();
        var deadEntities = componentManager.GetPackedPool<DeadComponent>();
        var abilityScores = componentManager.GetPackedPool<AbilityScoresComponent>();

        systemManager.Register(new ManaRegenSystem(
            componentManager.GetPackedPool<ManaComponent>(),
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _processingTierEvents,
            statModifiers,
            deadEntities,
            abilityScores));
    }
}
