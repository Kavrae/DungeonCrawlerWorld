using Engine.Math;
using Game.Blueprints;
using Game.Spawning;

namespace Game.Admin;

/// <summary>A blueprint an admin command can name: its registry id and what to call it.</summary>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct BlueprintChoice(ushort BlueprintId, string Name);

/// <summary>Admin Mode's spawn and apply commands: every blueprint that can be spawned where the cursor is, or applied to an entity under it.</summary>
/// <remarks>
/// A debugging tool over EntityFactory, not a gameplay path. The lists are read from the registry
/// each time they are asked for, so a blueprint composed at runtime appears in them too.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class BlueprintAdminCommands(EntityFactory factory, BlueprintRegistry definitions)
{

    /// <summary>Every blueprint that declares enough to be spawned on its own, by name.</summary>
    public IReadOnlyList<BlueprintChoice> Spawnable() => Choices(static blueprint => blueprint.IsSpawnable);

    /// <summary>Every blueprint that can't be spawned alone but does something when built onto an entity -- a class, a trait -- by name.</summary>
    public IReadOnlyList<BlueprintChoice> Applicable() => Choices(IsApplicable);

    /// <summary>Spawns blueprintId at position, on position's layer; false when it landed off the map.</summary>
    public bool Spawn(ushort blueprintId, Vector3Int position) =>
        factory.Spawn(SpawnRequest.At(blueprintId, position)) != EntityFactory.NoEntity;

    /// <inheritdoc cref="EntityFactory.Apply"/>
    public void Apply(int entityId, ushort blueprintId) => factory.Apply(entityId, blueprintId);

    /// <summary>Not spawnable, and has something to build: a blueprint, a race or class, actions or occupancy -- a definition that only declares appearance would change nothing on a live entity.</summary>
    private bool IsApplicable(ResolvedBlueprint blueprint)
    {
        if (blueprint.IsSpawnable)
        {
            return false;
        }

        foreach (var partId in blueprint.BuildOrder)
        {
            var part = definitions.Get(partId);
            if (part.Build is not null || part.Race is not null || part.Class is not null || part.Actions.Count > 0 || part.NonBlocking is not null)
            {
                return true;
            }
        }

        return false;
    }

    private List<BlueprintChoice> Choices(Func<ResolvedBlueprint, bool> include)
    {
        var choices = new List<BlueprintChoice>();
        for (var id = (ushort)1; id <= definitions.Count; id++)
        {
            var blueprint = definitions.Resolve(id);
            if (include(blueprint))
            {
                choices.Add(new BlueprintChoice(id, blueprint.Definition.Name));
            }
        }

        choices.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        return choices;
    }
}
