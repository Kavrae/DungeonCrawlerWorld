using Game.Modules.Actions;
using Presentation.UI.Content;

namespace Presentation.Input.DragDrop;

/// <summary>A hotbar slot, as what a content drop lands on -- named by its HotbarSlotElement.</summary>
public sealed record HotbarSlotDropTarget(HotbarContent Hotbar, HotkeySlot Slot);
