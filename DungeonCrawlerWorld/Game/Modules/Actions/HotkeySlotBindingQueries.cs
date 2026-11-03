using Engine.ECS.Components;
using Engine.ECS.Components.Stores;

namespace Game.Modules.Actions;

/// <summary>
/// Generic "match by HotkeySlot" matcher for any IHotkeySlotBinding component pool -- shared
/// implementation behind ActionHotkeyBindingQueries and Game.Modules.Inventory.
/// ItemHotkeyBindingQueries, which were previously two copies of this exact chain-walk +
/// matcher. Those two classes stay as the public, better-named call sites (TryGet's out
/// parameter reads as actionId/itemDefinitionId there, not the generic BoundId); this class only
/// exists so the underlying MultiComponentPool walk is written once.
/// </summary>
public static class HotkeySlotBindingQueries
{
    public static bool TryGet<T, TBoundId>(MultiComponentPool<T> bindings, int entityId, HotkeySlot slot, out TBoundId boundId) where T : struct, IHotkeySlotBinding<TBoundId>
    {
        var found = bindings.TryGetFirst(entityId, new SlotMatcher<T, TBoundId>(slot), out var binding);
        boundId = found ? binding.BoundId : default!;
        return found;
    }

    public static void Unbind<T, TBoundId>(MultiComponentPool<T> bindings, int entityId, HotkeySlot slot) where T : struct, IHotkeySlotBinding<TBoundId> =>
        bindings.RemoveFirst(entityId, new SlotMatcher<T, TBoundId>(slot));

    private readonly struct SlotMatcher<T, TBoundId>(HotkeySlot slot) : IComponentMatcher<T> where T : struct, IHotkeySlotBinding<TBoundId>
    {
        public bool Matches(in T binding) => binding.Slot == slot;
    }
}
