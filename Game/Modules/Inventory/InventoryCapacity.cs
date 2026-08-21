using Engine.ECS.Components;
using Game.Modules.Inventory.Components;
using Game.World;

namespace Game.Modules.Inventory;

/// <summary>
/// The "how many distinct stacks can this entity hold" rule -- unlimited for the player, capped for
/// everyone else (a temporary flat cap until item weight/carry capacity scaling with Strength
/// lands, see TODO.md). Counts distinct InventoryItemStackComponent instances, not summed
/// Quantity -- MultiComponentPool.CountForEntity is already exactly that count. Currently wired
/// into InventoryActions' stack-transfer methods only -- nothing else grants a non-player entity a
/// *new* distinct stack yet (no loot table, no mob pickup), so this is the one real call site
/// today; a future loot table or mob-pickup system should reuse this same helper rather than
/// duplicating the check.
/// </summary>
public static class InventoryCapacity
{
    public const int MaxNonPlayerStackCount = 20;

    public static bool HasRoomForNewStack(ComponentManager componentManager, int entityId, IPlayerQuery? playerQuery) =>
        entityId == playerQuery?.PlayerEntityId ||
        componentManager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(entityId) < MaxNonPlayerStackCount;

    public static bool HasRoomForNewStacks(ComponentManager componentManager, int entityId, IPlayerQuery? playerQuery, int additionalStackCount) =>
        entityId == playerQuery?.PlayerEntityId ||
        componentManager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(entityId) + additionalStackCount <= MaxNonPlayerStackCount;
}
