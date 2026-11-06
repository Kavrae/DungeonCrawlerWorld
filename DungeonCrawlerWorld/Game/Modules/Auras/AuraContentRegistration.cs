using Game.Effects;

namespace Game.Modules.Auras;

/// <summary>Registers every aura the build's terrain, blueprints and content name, so each has an id before anything radiates it.</summary>
/// <remarks>
/// <para>
/// Run once every module is configured. Every aura is a record of the content's Auras table, and no module registers
/// one, so this is where the catalog is filled: every terrain's aura and every blueprint's, named by Guid and read from
/// the content's winning record, and every aura an effect entry names -- an action's, an item's, a terrain contact's,
/// or another aura's own. Doing it here rather than on first use is what lets a worker planning a neighborhood read
/// ids without ever adding one, and what lets the content checks that follow see every aura.
/// </para>
/// <para>
/// Two different definitions sharing one Guid fail the build: the catalog keeps the first it is given, so the other
/// would radiate effects and a colour it doesn't declare. Content gives one definition per Guid, so only a definition
/// built in code can clash.
/// </para>
/// </remarks>
internal static class AuraContentRegistration
{
    /// <exception cref="InvalidOperationException">Two different definitions share a Guid, naming both; or a terrain or blueprint names an aura no content layer holds.</exception>
    public static void RegisterAll(GameModuleContext context)
    {
        var auras = context.Auras;
        var terrain = context.Terrain;
        var content = context.Content;
        var definitionsByGuid = new Dictionary<Guid, AuraDefinition>();

        AuraDefinition Load(Guid auraId, string heldBy) =>
            content.TryLoadAura(auraId, out var aura)
                ? aura
                : throw new InvalidOperationException($"{heldBy} radiates the aura {auraId}, which no content layer's Auras table holds.");

        void Register(AuraDefinition aura)
        {
            if (definitionsByGuid.TryGetValue(aura.Id, out var registered))
            {
                if (!ReferenceEquals(registered, aura) && !registered.Equals(aura))
                {
                    throw new InvalidOperationException(SharedGuidProblem(aura, registered));
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
                Register(Load(terrainAura.AuraId, $"Terrain '{terrainDefinition.Key}'"));
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
                Register(Load(grant.AuraId, $"Blueprint '{blueprint.Name}'"));
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

    /// <summary>Every aura of named that the catalog already holds a different definition of under its Guid, or that it has no room left for, as a sentence naming heldBy.</summary>
    /// <remarks>The per-record form of the shared-Guid rule, for a definition made at runtime: its auras are checked against the session's catalog rather than the content around it.</remarks>
    public static IReadOnlyList<string> ProblemsOf(AuraCatalog auras, IEnumerable<AuraDefinition> named, string heldBy)
    {
        var problems = new List<string>();
        var newGuids = new HashSet<Guid>();
        foreach (var aura in named)
        {
            if (auras.TryGetId(aura.Id, out var auraId))
            {
                var registered = auras.Get(auraId);
                if (!ReferenceEquals(registered, aura) && !registered.Equals(aura))
                {
                    problems.Add($"{heldBy} names an aura the session already holds another definition of: {SharedGuidProblem(aura, registered)}");
                }
            }
            else
            {
                newGuids.Add(aura.Id);
            }
        }

        if (auras.Count + newGuids.Count > AuraCatalog.Capacity)
        {
            problems.Add($"{heldBy} names {newGuids.Count} new auras, more than the {AuraCatalog.Capacity - auras.Count} the session has room for.");
        }

        return problems;
    }

    private static string SharedGuidProblem(AuraDefinition aura, AuraDefinition registered) =>
        $"Aura '{aura.Name}' and aura '{registered.Name}' are different definitions sharing the Guid {aura.Id}. Two sources of one aura share one definition; two different auras need their own Guids.";
}
