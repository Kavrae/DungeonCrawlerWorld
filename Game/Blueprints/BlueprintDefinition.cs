using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.Lootboxes;

namespace Game.Blueprints;

/// <summary>One composable entity building block: the blueprints it includes, the facets it grants and the components it writes itself.</summary>
/// <remarks>
/// Every entity is spawned from one definition, and a definition can include any number of others,
/// themselves composites -- a goblin engineer is Goblin and Engineer plus its own step, a goblin
/// foreman is that plus Boss. BlueprintRegistry flattens the includes into one build order
/// (ResolvedBlueprint): each included definition is built before the one including it, in list order,
/// and a definition reached twice is built once, where it was first reached.
///
/// What must be known without building an entity is declared here rather than written by the
/// blueprint: its appearance, race (body plan), class, occupancy, shared actions and where and how
/// big it spawns by default. Blueprint writes only the per-entity state a build creates.
/// </remarks>
/// <param name="Id">The stable identity mods replace a definition by, and what a save persists.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record BlueprintDefinition(Guid Id, string Name)
{
    /// <summary>The definitions built before this one, in order, by Guid.</summary>
    public IReadOnlyList<Guid> Includes { get; init; } = [];

    /// <summary>Writes the components this definition builds itself, after everything it includes, or null for one that only composes or declares.</summary>
    /// <remarks>
    /// A blueprint's own private static Build method, so it can hold no state: everything a build depends
    /// on arrives in its BlueprintContext, and one method serves every world and every seed -- the real
    /// world and SpawnRecordRebuilder' staging world alike. Blueprints compose, so a later part may write a
    /// component an earlier one already wrote: write with Merge rather than Add, which combines with an
    /// earlier write instead of throwing against it, and TryUpdate where a real override is meant.
    /// </remarks>
    public Action<BlueprintContext>? Build { get; init; }

    /// <summary>How an entity built with this definition looks and is called, as far as this definition says.</summary>
    public AppearanceFacet? Appearance { get; init; }

    /// <summary>Set when building this definition makes an entity that race.</summary>
    public RaceFacet? Race { get; init; }

    /// <summary>Set when building this definition gives an entity that class.</summary>
    public ClassFacet? Class { get; init; }

    /// <summary>Set when this definition makes an entity never block its cell, with how the map draws it; held from spawn, since occupancy can't wait for a build.</summary>
    public NonBlockingKind? NonBlocking { get; init; }

    /// <summary>The actions every entity built with this definition can use, held here rather than per entity -- see ActionGrant.</summary>
    public IReadOnlyList<ActionGrant> Actions { get; init; } = [];

    /// <summary>The MapLayer an entity of it spawns on when the spawn names none.</summary>
    public MapLayer? Layer { get; init; }

    /// <summary>The footprint an entity of it spawns with when the spawn names none.</summary>
    public Vector2Byte? Size { get; init; }

    /// <summary>The loot box the player is granted for landing the killing blow on an entity built with this definition, or null for none -- see BossLootboxAwarder.</summary>
    /// <remarks>Declared rather than built, so an entity that died before it was ever built past a skeleton still pays out.</remarks>
    public LootboxReward? Lootbox { get; init; }
}

/// <summary>What makes a definition a race: the body plan every creature of it shares.</summary>
/// <param name="BodyParts">The race's body plan, or empty for a race that uses simple health -- see EntityBodyParts.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record RaceFacet(BodyPartTemplate[]? BodyParts = null)
{
    /// <remarks>An array rather than a list: EntityBodyParts indexes it once per part on hot paths, where interface dispatch showed up in the benchmark.</remarks>
    public BodyPartTemplate[] BodyParts { get; init; } = BodyParts ?? [];
}

/// <summary>What makes a definition a class. A class's name follows the entity's own in the name it composes.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed record ClassFacet;
