using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Inventory.Systems;

/// <summary>Ends item cooldowns on the frame they end: a cooling stack's units go back into an interchangeable ready stack.</summary>
/// <remarks>
/// A unit used with a cooldown sits in a stack of its own until then (InventoryItemStackComponent.CooldownReadyAtFrame,
/// a scheduled timer), so this touches only the stacks whose cooldown ends this frame. Merging moves the stack's units
/// into another, so every hotkey slot bound to a stack that is gone follows them, and so does the holder's windup when
/// it names that stack (a Delayed toggle item whose cooldown ends before its windup does), which would otherwise resolve
/// into a stack that no longer exists. A holder that isn't simulated is
/// skipped until it resumes, as every timer wheel does.
/// </remarks>
public sealed class ItemCooldownSystem : ISystem
{
    /// <summary>Every frame; the wheel only touches cooldowns actually ending.</summary>
    public byte StripeCount => 1;

    private readonly ComponentManager _componentManager;
    private readonly MultiTimerWheel<InventoryItemStackComponent> _wheel;
    private readonly MultiComponentPool<ItemHotkeyBindingComponent> _itemHotkeyBindings;
    private readonly PackedComponentPool<PendingWindupComponent> _pendingWindups;

    // Cached once: an instance method group conversion allocates a fresh delegate every evaluation.
    private readonly TimerFired<InventoryItemStackComponent> _endCooldown;

    public ItemCooldownSystem(ComponentManager componentManager, MultiComponentPool<ItemHotkeyBindingComponent> itemHotkeyBindings, PackedComponentPool<PendingWindupComponent> pendingWindups, SimulationScope simulationScope)
    {
        _pendingWindups = pendingWindups;
        _componentManager = componentManager;
        _itemHotkeyBindings = itemHotkeyBindings;
        _endCooldown = EndCooldown;
        _wheel = new MultiTimerWheel<InventoryItemStackComponent>(componentManager.GetMultiPool<InventoryItemStackComponent>(), simulationScope);
    }

    public void Update(EngineTime time, byte stripeIndex) => _wheel.Tick(time.FrameCount, _endCooldown);

    /// <summary>Always returns false: the stack is merged away or kept ready here, never removed by the wheel.</summary>
    private bool EndCooldown(int entityId, InventoryItemStackComponent stack, long now)
    {
        var stackInstanceId = stack.StackInstanceId;
        var heldStackInstanceId = InventoryActions.EndCooldown(_componentManager, entityId, stackInstanceId);
        if (heldStackInstanceId != 0 && heldStackInstanceId != stackInstanceId)
        {
            ItemHotkeyBindingActions.RepointAfterUnitMoved(_itemHotkeyBindings, entityId, stackInstanceId, heldStackInstanceId, activatedFromSlot: null, oldStackStillHeld: false);
            RepointWindup(entityId, stackInstanceId, heldStackInstanceId);
        }

        return false;
    }

    /// <summary>Points entityId's windup at heldStackInstanceId when it names the stack that was merged away.</summary>
    private void RepointWindup(int entityId, uint mergedStackInstanceId, uint heldStackInstanceId)
    {
        if (!_pendingWindups.TryGetReadonly(entityId, out var pending) || pending.Activatable != ActivatableReference.ItemStack(mergedStackInstanceId))
        {
            return;
        }

        using var windup = _pendingWindups.Write(entityId);
        windup.Value.Activatable = ActivatableReference.ItemStack(heldStackInstanceId);
    }
}
