using Engine.Modules;
using Game.Modules.Race.Components;

namespace Game.Modules.Race;

public sealed class RaceModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000005");

    public Guid Id => ModuleId;

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;


        componentManager.RegisterPackedPool<RaceSlotsComponent>(static (ref existing, incoming) =>
        {
            existing.Add(incoming.Race1);
            existing.Add(incoming.Race2);
        }, initialCapacity: 80_000);
    }

    public void RegisterSystems(SystemRegistration<GameModuleContext> registration)
    {

        // No systems of its own -- Race is narrative/display data today, consulted by
        // other systems rather than driving its own per-frame behavior.
    }
}