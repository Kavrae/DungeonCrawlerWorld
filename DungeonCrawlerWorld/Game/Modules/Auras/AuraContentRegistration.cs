using Game.Effects;

namespace Game.Modules.Auras;

/// <summary>Registers every aura definition the build's content names, so each has an id before anything radiates it.</summary>
/// <remarks>
/// <para>
/// Run once every module is configured. A definition lives with its source and no module registers
/// one, so this is where the catalog is filled: from every terrain's aura, every blueprint's auras,
/// and every aura an effect entry names -- an action's, an item's, a terrain contact's, or another
/// aura's own. Doing it here rather than on first use is what lets a worker planning a neighborhood
/// read ids without ever adding one, and what lets the content checks that follow see every aura.
/// </para>
/// <para>
/// Two different definitions in the content sharing one Guid fail the build. Each source registers
/// its own definition again when it radiates, so the two would take turns replacing each other in
/// the catalog -- each turn a definition change that rescans every source in the world, and each
/// aura radiating the other's effects and colour in between.
/// </para>
/// </remarks>
internal static class AuraContentRegistration
{
    /// <exception cref="InvalidOperationException">Two different definitions share a Guid, naming both.</exception>
    public static void RegisterAll(GameModuleContext context)
    {
        var auras = context.Auras;
        var terrain = context.Terrain;
        var definitionsByGuid = new Dictionary<Guid, AuraDefinition>();

        void Register(AuraDefinition aura)
        {
            if (definitionsByGuid.TryGetValue(aura.Id, out var registered))
            {
                if (!ReferenceEquals(registered, aura) && !registered.Equals(aura))
                {
                    throw new InvalidOperationException($"Aura '{aura.Name}' and aura '{registered.Name}' are different definitions sharing the Guid {aura.Id}. Two sources of one aura share one definition; two different auras need their own Guids.");
                }

                return;
            }

            definitionsByGuid.Add(aura.Id, aura);
            auras.Register(aura);
        }

        for (var terrainTypeId = 1; terrainTypeId <= terrain.Count; terrainTypeId++)
        {
            if (terrain.TryGet((ushort)terrainTypeId, out var terrainDefinition) && terrainDefinition.Aura is { } terrainAura)
            {
                Register(terrainAura.Aura);
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
                Register(grant.Aura);
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
                    Register(aura);
                }
            });
        }
        while (auras.Count != registeredBefore);
    }
}
