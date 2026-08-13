using Game.Modules.Actions;

namespace Game.World;

/// <summary>
/// Published by HotbarContent.BindItem -- the real click-and-drag hotbar binding path -- every
/// time an item gets bound to a slot. Not published by PlayerBlueprint's own hardcoded starting
/// binds (spawn-time setup, not a player action) -- see ArchivistAchievement, the one consumer.
/// </summary>
public readonly record struct ItemHotkeyBoundEvent(int EntityId, HotkeySlot Slot, Guid ItemDefinitionId);
