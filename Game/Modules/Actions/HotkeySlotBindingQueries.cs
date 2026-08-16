using Engine.ECS.Components.Stores;

namespace Game.Modules.Actions;

/// <summary>
/// Generic "match by HotkeySlot" predicate for any IHotkeySlotBinding component pool -- shared
/// implementation behind ActionHotkeyBindingQueries and Game.Modules.Inventory.
/// ItemHotkeyBindingQueries, which were previously two copies of this exact chain-walk +
/// predicate. Those two classes stay as the public, better-named call sites (TryGet's out
/// parameter reads as actionId/itemDefinitionId there, not the generic BoundId); this class only
/// exists so the underlying MultiComponentPool walk is written once.
/// </summary>
public static class HotkeySlotBindingQueries
{
    public static bool TryGet<T>(MultiComponentPool<T> bindings, int entityId, HotkeySlot slot, out Guid boundId) where T : struct, IHotkeySlotBinding
    {
        var found = bindings.TryGetFirst(entityId, slot, static (ref readonly T candidate, HotkeySlot s) => candidate.Slot == s, out var binding);
        boundId = found ? binding.BoundId : default;
        return found;
    }

    public static void Unbind<T>(MultiComponentPool<T> bindings, int entityId, HotkeySlot slot) where T : struct, IHotkeySlotBinding =>
        bindings.RemoveFirst(entityId, slot, static (ref readonly binding, s) => binding.Slot == s);
}
