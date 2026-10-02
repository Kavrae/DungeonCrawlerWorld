using Game.Effects;

namespace Game.Modules.Auras;

/// <summary>Registers every aura definition the build's content names, so each has an id before anything radiates it.</summary>
/// <remarks>
/// Run once every module is configured. A definition lives with its source and no module registers
/// one, so this is where the catalog is filled: from every terrain's aura, every blueprint's auras,
/// and every aura an effect entry names -- an action's, an item's, a terrain contact's, or another
/// aura's own. Doing it here rather than on first use is what lets a worker planning a neighborhood
/// read ids without ever adding one, and what lets the content checks that follow see every aura.
/// </remarks>
internal static class AuraContentRegistration
{
    public static void RegisterAll(GameModuleContext context)
    {
        var auras = context.Auras;
        var terrain = context.Terrain;

        for (var terrainTypeId = 1; terrainTypeId <= terrain.Count; terrainTypeId++)
        {
            if (terrain.TryGet((ushort)terrainTypeId, out var terrainDefinition) && terrainDefinition.Aura is { } terrainAura)
            {
                auras.Register(terrainAura.Aura);
            }
        }

        var blueprints = context.Definitions;
        for (var blueprintId = 1; blueprintId <= blueprints.Count; blueprintId++)
        {
            if (!blueprints.TryGet((ushort)blueprintId, out var blueprint))
            {
                continue;
            }

            foreach (var grant in blueprint.Auras)
            {
                auras.Register(grant.Aura);
            }
        }

        // An aura's own effects can name another aura, which this pass has then only just registered:
        // repeat until a pass over every holder, the catalog included, adds nothing.
        int registeredBefore;
        do
        {
            registeredBefore = auras.Count;
            EffectContent.ForEachEntry(context, (entry, _) =>
            {
                foreach (var aura in entry.ReferencedAuras)
                {
                    auras.Register(aura);
                }
            });
        }
        while (auras.Count != registeredBefore);
    }
}
