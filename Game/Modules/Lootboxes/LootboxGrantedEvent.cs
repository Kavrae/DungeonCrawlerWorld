namespace Game.Modules.Lootboxes;

/// <summary>Published whenever an entity is granted loot boxes.</summary>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct LootboxGrantedEvent(int EntityId, LootboxReward Reward, ushort Quantity);
