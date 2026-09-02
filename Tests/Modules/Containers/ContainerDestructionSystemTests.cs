using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.Containers.Components;
using Game.Modules.Containers.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Inventory.Components;
using Game.World;

namespace Tests.Modules.Containers;

[TestClass]
public sealed class ContainerDestructionSystemTests
{
    private static (ContainerDestructionSystem System, PackedComponentPool<ContainerComponent> Containers, MultiComponentPool<InventoryItemStackComponent> InventoryStacks, DirectComponentPool<DisplayTextComponent> DisplayText, EventBus EventBus) Build()
    {
        var containers = new PackedComponentPool<ContainerComponent>(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);
        var inventoryStacks = new MultiComponentPool<InventoryItemStackComponent>(maximumEntityCount: 10, initialCapacity: 4);
        var displayText = new DirectComponentPool<DisplayTextComponent>(10, static (ref existing, incoming) => existing = incoming);
        var eventBus = new EventBus();

        var system = new ContainerDestructionSystem(containers, inventoryStacks, displayText, eventBus);

        return (system, containers, inventoryStacks, displayText, eventBus);
    }

    [TestMethod]
    public void EntityDied_IsContainer_ClearsInventory()
    {
        var (_, containers, inventoryStacks, displayText, eventBus) = Build();
        containers.Add(0, new ContainerComponent());
        inventoryStacks.Add(0, new InventoryItemStackComponent(Guid.NewGuid(), quantity: 5));
        inventoryStacks.Add(0, new InventoryItemStackComponent(Guid.NewGuid(), quantity: 3));
        displayText.Add(0, new DisplayTextComponent("Treasure Chest", "A sturdy chest."));

        eventBus.Publish(new EntityDiedEvent(0, StatusEffectSource.FromEntity(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.IsFalse(inventoryStacks.Has(0));
    }

    [TestMethod]
    public void EntityDied_IsContainer_RenamesToDestroyed()
    {
        var (_, containers, _, displayText, eventBus) = Build();
        containers.Add(0, new ContainerComponent());
        displayText.Add(0, new DisplayTextComponent("Treasure Chest", "A sturdy chest that might hold treasure."));

        eventBus.Publish(new EntityDiedEvent(0, StatusEffectSource.FromEntity(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        var renamed = displayText.GetReadonly(0);
        Assert.AreEqual("Destroyed", renamed.Name);
        Assert.AreNotEqual("A sturdy chest that might hold treasure.", renamed.Description);
    }

    [TestMethod]
    public void EntityDied_NotAContainer_LeavesInventoryAndNameUntouched()
    {
        var (_, _, inventoryStacks, displayText, eventBus) = Build();
        inventoryStacks.Add(0, new InventoryItemStackComponent(Guid.NewGuid(), quantity: 5));
        displayText.Add(0, new DisplayTextComponent("Goblin", "Small, green and smart."));

        eventBus.Publish(new EntityDiedEvent(0, StatusEffectSource.FromEntity(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.IsTrue(inventoryStacks.Has(0));
        Assert.AreEqual("Goblin", displayText.GetReadonly(0).Name);
    }
}
