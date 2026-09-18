namespace Game.World;

/// <summary>
/// Narrow read contract for "who is the player" -- deliberately separate from IMapQuery (which
/// is position/map-only, see its own doc comment) since not every module needing this needs
/// map queries too. World implements this directly. A live reference (not a value captured
/// once), since Configure runs before the player entity exists -- see GameBootstrapper.Build.
/// </summary>
public interface IPlayerQuery
{
    int PlayerEntityId { get; }

    /// <summary>The player's stable key -- what a stored ActionSource is compared against, since a source never holds a runtime id.</summary>
    Engine.ECS.Entities.EntityKey PlayerEntityKey { get; }
}
