using Engine.ECS.Entities;
using Game.World;

namespace Builders.Editing;

/// <summary>The Workshop's player query: there is none, so a details window shows each definition's own values -- no ability-score bonus, no Intelligence scaling, no modifiers -- whatever its session holds.</summary>
/// <remarks>Who a value is worked out for is TODO.md's "Workshop Test Preview": premade caster and target entities.</remarks>
public sealed class NoWorkshopPlayer : IPlayerQuery
{
    /// <summary>The entity id that stands for "no player".</summary>
    public const int NoPlayerEntityId = -1;

    public static NoWorkshopPlayer Instance { get; } = new();

    public int PlayerEntityId => NoPlayerEntityId;

    public EntityKey PlayerEntityKey => EntityKey.None;
}
