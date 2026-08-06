using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.AbilityScores.Components;
using Game.Modules.StatModifiers;

namespace Game.Modules.AbilityScores;

/// <summary>
/// Registers AbilityScoreComponent and keeps its Total in sync with StatModifiersModule --
/// deliberately has no ISystem/StripeCount of its own: Total is precomputed eagerly at the two
/// moments it can actually change (AbilityScoreEffects.GrantModifier, called inline, and
/// StatModifierExpiredEvent, subscribed here), not polled every frame across every entity that
/// has ability scores. See AbilityScoreComponent's own doc comment for why a periodic poll would
/// be the wrong tradeoff at the entity counts GameLoop.InitialEntityCapacity is sized for.
/// </summary>
public sealed class AbilityScoresModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000013");

    public IReadOnlyList<Type> Dependencies { get; } = [typeof(StatModifiersModule)];

    private EventBus _eventBus = null!;

    public void Configure(GameModuleContext context) => _eventBus = context.EventBus;

    public void RegisterComponents(ComponentManager componentManager) =>
        componentManager.RegisterMultiPool<AbilityScoreComponent>();

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager) =>
        _eventBus.Subscribe<StatModifierExpiredEvent>(expired =>
            AbilityScoreEffects.RecomputeIfAbilityScore(componentManager, expired.EntityId, expired.Target));
}
