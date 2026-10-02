using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Core.Components;
using Game.Modules.Lootboxes;

namespace Game.Blueprints;

/// <summary>A definition with its includes flattened: the order its parts build in, and what the entity it spawns is, computed once.</summary>
/// <remarks>Every reader of "what is this entity" -- its races, classes, actions, look, name and occupancy -- reads this, so none of them walks the includes itself.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ResolvedBlueprint
{
    private readonly ActionGrant[] _actions;

    internal ResolvedBlueprint(BlueprintDefinition definition, ushort[] buildOrder, ushort[] races, ushort[] classes, ActionGrant[] actions, AuraGrant[] auras, NonBlockingKind[] nonBlocking, EntityAppearance appearance, MapLayer layer, Vector2Byte size, LootboxReward? lootbox)
    {
        Lootbox = lootbox;
        Layer = layer;
        Size = size;
        Definition = definition;
        BuildOrder = buildOrder;
        Races = races;
        Classes = classes;
        _actions = actions;
        Auras = auras;
        NonBlocking = nonBlocking;
        Appearance = appearance;
    }

    public BlueprintDefinition Definition { get; }

    /// <summary>Every definition this one builds, itself last, as registry ids.</summary>
    public IReadOnlyList<ushort> BuildOrder { get; }

    /// <summary>The races it builds, in build order.</summary>
    public IReadOnlyList<ushort> Races { get; }

    /// <summary>The classes it builds, in build order.</summary>
    public IReadOnlyList<ushort> Classes { get; }

    /// <summary>Every action its definitions grant, one per action: a later definition's grant of an action replaces an earlier one's, so a composite can override what a race gives.</summary>
    public IReadOnlyList<ActionGrant> Actions => _actions;

    /// <summary>Every aura its definitions grant, one per aura: a later definition's grant of an aura replaces an earlier one's.</summary>
    public IReadOnlyList<AuraGrant> Auras { get; }

    /// <summary>Each occupancy kind a part declares, in build order.</summary>
    public IReadOnlyList<NonBlockingKind> NonBlocking { get; }

    public EntityAppearance Appearance { get; }

    /// <summary>The MapLayer an entity of it spawns on unless the spawn names one: the last definition to declare one, a second race aside; Ground when none does.</summary>
    public MapLayer Layer { get; }

    /// <summary>The footprint an entity of it spawns with unless the spawn names one, decided the same way as Layer; 1x1 when none does.</summary>
    public Vector2Byte Size { get; }

    /// <summary>The loot box its killer is granted: the last definition in build order to declare one; null when none does.</summary>
    public LootboxReward? Lootbox { get; }

    /// <summary>Whether it declares everything an entity needs to be drawn and named -- a trait (Boss, Tiny) doesn't, and is only ever built as part of something else.</summary>
    public bool IsSpawnable => Appearance.Missing.Count == 0;

    /// <summary>Whether an entity of it can be spawned unbuilt: only a creature, a blueprint holding a race, is ever left as a skeleton.</summary>
    public bool Deferrable => Races.Count > 0;

    /// <inheritdoc cref="EntityAppearance.NameFor"/>
    public string NameFor(uint seed) => Appearance.NameFor(seed);

    /// <remarks>A scan of an array a handful long, on the path every action activation and NPC decision takes.</remarks>
    public bool TryGetAction(Guid actionId, out ActionGrant grant)
    {
        foreach (var candidate in _actions)
        {
            if (candidate.ActionId == actionId)
            {
                grant = candidate;
                return true;
            }
        }

        grant = null!;
        return false;
    }}
