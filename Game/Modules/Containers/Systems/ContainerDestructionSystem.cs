using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Containers.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory.Components;
using Game.World;

namespace Game.Modules.Containers.Systems;

/// <summary>
/// Reacts to "a container died": subscribes to EntityDiedEvent (published by HealthDamage.Apply
/// for any dying SimpleHealthComponent entity, regardless of type) once at construction --
/// DeathSystem is the one dedicated system that actually calls DispatchBuffered&lt;EntityDiedEvent&gt;
/// each frame (see EntityDiedEvent's own doc comment), so this system has nothing of its own to
/// do on Update; it only reacts when that shared dispatch fires, the same way
/// KilledAMobAchievement's own EntityDiedEvent subscription does. Only entities carrying
/// ContainerComponent are affected: their inventory is wiped (see TODO.md's Destroyed items entry
/// for the eventual "mark destroyed instead of delete" follow-up) and their DisplayTextComponent
/// is overwritten to "Destroyed" -- a creature's corpse keeps its name/inventory intact, a
/// destroyed container does not.
/// </summary>
public sealed class ContainerDestructionSystem : ISystem
{
    public byte StripeCount => 1;

    private const string DestroyedName = "Destroyed";
    private const string DestroyedDescription = "The remains of a destroyed container. Whatever it once held is gone.";

    private readonly PackedComponentPool<ContainerComponent> _containers;
    private readonly MultiComponentPool<InventoryItemStackComponent> _inventoryStacks;
    private readonly DirectComponentPool<DisplayTextComponent> _displayText;

    public ContainerDestructionSystem(
        PackedComponentPool<ContainerComponent> containers,
        MultiComponentPool<InventoryItemStackComponent> inventoryStacks,
        DirectComponentPool<DisplayTextComponent> displayText,
        EventBus eventBus)
    {
        _containers = containers;
        _inventoryStacks = inventoryStacks;
        _displayText = displayText;

        eventBus.Subscribe<EntityDiedEvent>(OnEntityDied);
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
    }

    private void OnEntityDied(EntityDiedEvent died)
    {
        if (!_containers.Has(died.EntityId))
        {
            return;
        }

        _inventoryStacks.Remove(died.EntityId);
        _displayText.TryUpdate(died.EntityId, static (ref DisplayTextComponent displayText) =>
        {
            displayText.Name = DestroyedName;
            displayText.Description = DestroyedDescription;
        });
    }
}
