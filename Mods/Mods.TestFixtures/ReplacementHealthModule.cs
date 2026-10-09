using Engine.Modules;
using Game.Modules;
using Game.Modules.Health;
using Game.Modules.Health.Components;

namespace Mods.TestFixtures;

/// <summary>
/// Replaces the built-in HealthModule by its Id: registers the same components and no systems, so
/// every entity keeps its health but nothing regenerates it -- a fixture for proving
/// mod-replaces-built-in end to end, observable as HealthModule's regen systems being absent while
/// everything that reads health still builds.
///
/// Kept out of Mods.ExampleMod (the plan's shippable trivial-mod fixture) because dropping it into a
/// running game's Mods/ folder would switch regeneration off: GameBootstrapperTests exercises this at
/// the GameBootstrapper.Build level only.
/// </summary>
public sealed class ReplacementHealthModule : IGameModule
{
    public Guid Id => HealthModule.ModuleId;

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<BodyPartStateComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<DamageContributionComponent>();
        componentManager.RegisterPackedPool<DamageLedgerExpiryComponent>(static (ref existing, incoming) => existing = incoming);
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration) { }
}
