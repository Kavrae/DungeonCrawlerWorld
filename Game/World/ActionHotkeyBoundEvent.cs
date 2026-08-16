using Game.Modules.Actions;

namespace Game.World;

/// <summary>Represents an event that is published when an action is bound to a hotkey slot.</summary>
/// <param name="EntityId">The ID of the entity to which the action is bound.</param>
/// <param name="Slot">The hotkey slot to which the action is bound.</param>
/// <param name="ActionId">The ID of the action that is bound.</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct ActionHotkeyBoundEvent(int EntityId, HotkeySlot Slot, Guid ActionId);
