using Game.Views;

namespace Presentation.Input;

/// <summary>For one entity, the key of the hotbar slot each of its actions, item stacks and menu commands is bound to -- what an Actions or Inventory cell shows over its icon.</summary>
/// <remarks>
/// Refresh reads every slot once (HotkeySlotLayout's order); something bound to several slots shows the first one's key.
/// A slot with no key would show nothing, so a hotbar without keys never labels anything.
/// </remarks>
public sealed class BoundHotkeyLabels(HotkeyBindingView hotkeyBindingView)
{
    private readonly Dictionary<Guid, string> _actionKeyLabels = [];
    private readonly Dictionary<uint, string> _itemStackKeyLabels = [];
    private readonly Dictionary<Guid, string> _menuCommandKeyLabels = [];

    /// <summary>Reads entityId's bindings now.</summary>
    public void Refresh(int entityId)
    {
        _actionKeyLabels.Clear();
        _itemStackKeyLabels.Clear();
        _menuCommandKeyLabels.Clear();

        foreach (var entry in HotkeySlotLayout.Entries)
        {
            var keyLabel = HotkeySlotLayout.GetKeyLabel(entry.Slot);
            if (hotkeyBindingView.TryGetBoundAction(entityId, entry.Slot, out var actionId))
            {
                _actionKeyLabels.TryAdd(actionId, keyLabel);
            }
            else if (hotkeyBindingView.TryGetBoundItem(entityId, entry.Slot, out var stackInstanceId))
            {
                _itemStackKeyLabels.TryAdd(stackInstanceId, keyLabel);
            }
            else if (hotkeyBindingView.TryGetBoundMenuCommand(entityId, entry.Slot, out var menuCommandId))
            {
                _menuCommandKeyLabels.TryAdd(menuCommandId, keyLabel);
            }
        }
    }

    public string? ForAction(Guid actionId) => _actionKeyLabels.GetValueOrDefault(actionId);

    public string? ForItemStack(uint stackInstanceId) => _itemStackKeyLabels.GetValueOrDefault(stackInstanceId);

    public string? ForMenuCommand(Guid menuCommandId) => _menuCommandKeyLabels.GetValueOrDefault(menuCommandId);
}
