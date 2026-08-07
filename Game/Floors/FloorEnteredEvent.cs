namespace Game.Floors;

/// <summary>
/// Published once by GameLoop right after _playerSpawned flips true, alongside
/// Game.World.EnteredDungeonEvent. No consumers yet -- floors are strictly sequential today with no
/// advance trigger (see FloorBuilder's own doc comment), so this is wired ahead of anything
/// actually needing it.
/// </summary>
public readonly record struct FloorEnteredEvent(int FloorNumber);
