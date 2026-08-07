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

namespace Game.Modules.Mana;

/// <summary>Mirrors HealthModule's shape exactly -- see its own doc comment for why each optional pool stays optional rather than a hard Dependencies requirement.</summary>
public sealed class ManaModule : IGameModule
{
    public Guid Id { get; } = new("8e2a4f61-3c9d-4b7e-a1f5-6d8c2b9e4a71");

    public IReadOnlyList<Type> Dependencies { get; } = [];

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
        var statModifiers = componentManager.IsRegistered<StatModifierComponent>()
            ? componentManager.GetMultiPool<StatModifierComponent>()
            : null;
        var deadEntities = componentManager.IsRegistered<DeadComponent>()
            ? componentManager.GetPackedPool<DeadComponent>()
            : null;
        var abilityScores = componentManager.IsRegistered<AbilityScoreComponent>()
            ? componentManager.GetMultiPool<AbilityScoreComponent>()
            : null;

        systemManager.Register(new ManaRegenSystem(
            componentManager.GetPackedPool<ManaComponent>(),
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _processingTierEvents,
            statModifiers,
            deadEntities,
            abilityScores));
    }
}
