using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.AbilityScores.Components;
using Game.Modules.StatModifiers;
using Engine.Modules;

namespace Game.Modules.AbilityScores;

/// <summary>
/// Registers AbilityScoresComponent and keeps its Total in sync with StatModifiersModule --
/// deliberately has no ISystem/StripeCount of its own: Total is precomputed eagerly at the two
/// moments it can actually change (AbilityScoreEffects.GrantModifier, called inline, and
/// StatModifierExpiredEvent, subscribed here), not polled every frame across every entity that
/// has ability scores. See AbilityScoresComponent's own doc comment for why a periodic poll would
/// be the wrong tradeoff at the entity counts GameLoop.InitialEntityCapacity is sized for.
/// </summary>
public sealed class AbilityScoresModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000013");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [StatModifiersModule.ModuleId];

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterPackedPool<AbilityScoresComponent>(static (ref existing, incoming) => existing.MergeFrom(incoming), initialCapacity: 80_000);
    }

    public void RegisterSystems(SystemRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var componentManager = registration.ComponentManager;

        context.EventBus.Subscribe<StatModifierExpiredEvent>(expired =>
            AbilityScoreEffects.RecomputeIfAbilityScore(componentManager, expired.EntityId, expired.Target));
    }
}
