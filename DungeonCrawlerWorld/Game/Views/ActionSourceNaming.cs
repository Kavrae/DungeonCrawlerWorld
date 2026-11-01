using Engine.ECS.Entities;
using Game.Modules.Auras;
using Game.Terrain;
using Game.World;

namespace Game.Views;

/// <summary>The name to show a player for what caused an effect on an entity.</summary>
/// <remarks>
/// A source is the entity's own when it is the entity the line describes, compared by key, never by id: an id is
/// recycled the moment its entity is destroyed, so a dead entity's source never reads as the own source of whoever
/// reuses its id. An own source is named by the race, class, trait, action, spell or item it came through when one was
/// recorded (ActionSource.Cause), and "Self" otherwise. A terrain's
/// and an aura's name are read from their definition by id each time, so a definition replaced during the session
/// shows its new name. Any other entity is named as it was when the source was created (ActionSource.Identity), which
/// outlives the entity. ActionSource.ToString stays the debug form.
/// </remarks>
public sealed class ActionSourceNaming(TerrainRegistry terrain, AuraCatalog auras, EntityKeys entityKeys)
{
    public const string SelfName = "Self";

    /// <summary>Whether source is the entity describedEntityId itself.</summary>
    public bool IsOwnSource(ActionSource source, int describedEntityId) => source.IsEntity(entityKeys.GetKey(describedEntityId));

    /// <summary>The source on one line: an entity's own source by what it acted through ("Tank", "Fireball") when that was recorded, else "Self"; another entity's as "Toxic Strike (Goblin)", or the entity alone with nothing recorded.</summary>
    /// <param name="describedEntityId">The entity the effect is on -- the one the line describes.</param>
    public string Describe(ActionSource source, int describedEntityId) =>
        IsOwnSource(source, describedEntityId) ? OwnSourceName(source) : WithCause(source, DescribeOther(source));

    /// <summary>Describe, but an entity by its name alone, without its crawler number -- for a line with little room.</summary>
    /// <remarks>An entity recorded with no name is still described by its crawler number.</remarks>
    public string DescribeByName(ActionSource source, int describedEntityId)
    {
        if (IsOwnSource(source, describedEntityId))
        {
            return OwnSourceName(source);
        }

        return WithCause(source, source.Kind == ActionSourceKind.Entity && source.Identity.Name is { } name
            ? name
            : DescribeOther(source));
    }

    /// <summary>The source entity alone, never what it acted through: "Self" for describedEntityId's own source.</summary>
    public string DescribeEntity(ActionSource source, int describedEntityId) =>
        IsOwnSource(source, describedEntityId) ? SelfName : DescribeOther(source);

    /// <summary>What a cause of this kind is called on a labelled line: "Race", "Class", "Spell"...</summary>
    public static string CauseLabel(SourceCauseKind kind) => kind.ToString();

    private static string OwnSourceName(ActionSource source) => source.Cause.Name ?? SelfName;

    private static string WithCause(ActionSource source, string entityName) =>
        source.Kind == ActionSourceKind.Entity && source.Cause.Name is { } causeName ? $"{causeName} ({entityName})" : entityName;

    private string DescribeOther(ActionSource source) => source.Kind switch
    {
        ActionSourceKind.Entity => source.Identity.DisplayName,
        ActionSourceKind.Terrain when terrain.TryGet(source.TerrainTypeId, out var terrainDefinition) => terrainDefinition.Name,
        ActionSourceKind.Aura when source.AuraId < auras.Count => auras.Get(source.AuraId).Name,
        ActionSourceKind.Terrain or ActionSourceKind.Aura => source.ToString(),
        _ => source.Kind.ToString(),
    };
}
