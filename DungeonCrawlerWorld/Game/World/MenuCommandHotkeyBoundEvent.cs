using Game.Modules.Actions;

namespace Game.World;

/// <summary>Published when the player binds a menu command to a hotkey slot (HotkeyBindingCommands.BindMenuCommand); not by the player's kit's starting binds.</summary>
public readonly record struct MenuCommandHotkeyBoundEvent(int EntityId, HotkeySlot Slot, Guid MenuCommandId);
