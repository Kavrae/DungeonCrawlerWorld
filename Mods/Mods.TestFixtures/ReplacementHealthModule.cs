using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Modules;

namespace Mods.TestFixtures;

/// <summary>
/// Uses the built-in HealthModule's real Id (see Game.Modules.Health.HealthModule.Id), so
/// ModuleSet.Combine replaces it instead of adding this one alongside it -- a fixture for
/// proving mod-replaces-built-in end to end. Registers nothing at all, so the replacement is
/// observable: HealthComponent ends up unregistered only if this mod actually replaced
/// HealthModule rather than coexisting with it. HealthModule is deliberately chosen (over
/// e.g. CoreModule or EnergyModule) because nothing else declares a Dependencies entry on it,
/// so replacing it doesn't also break an unrelated built-in's dependency resolution.
///
/// Deliberately breaks any game content that assumes HealthComponent exists (e.g.
/// TestMapBuilder's Goblin blueprint) if actually dropped into a running game's Mods/ folder
/// -- that's the correct, expected consequence of the "friends sharing mod folders" trust
/// model (nothing validates a replacement preserves the built-in's contract), not a defect.
/// Kept out of Mods.ExampleMod (the plan's shippable trivial-mod fixture) for exactly that
/// reason: GameBootstrapperTests exercises this at the GameBootstrapper.Build level only.
/// </summary>
public sealed class ReplacementHealthModule : IModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000003");

    public void RegisterComponents(ComponentManager componentManager) { }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager) { }
}
