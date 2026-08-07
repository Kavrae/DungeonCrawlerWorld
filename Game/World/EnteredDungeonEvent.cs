namespace Game.World;

/// <summary>
/// Published once by GameLoop right after _playerSpawned flips true -- i.e. once
/// World.PlayerEntityId is already assigned, unlike the EntityMovedEvent spawn-sentinel this
/// replaces for achievement triggers (see LonerAchievement/UnarmedCombatAchievement), which
/// used to fire before that assignment. Carries no data: consumers that need the player's id
/// read it from IPlayerQuery at handler time instead.
/// </summary>
public readonly record struct EnteredDungeonEvent;
