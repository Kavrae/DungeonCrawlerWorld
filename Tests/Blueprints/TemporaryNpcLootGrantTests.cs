using Engine.ECS.Components;
using Engine.Math;
using Game.Blueprints.NPCs;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;

namespace Tests.Blueprints;

[TestClass]
public sealed class TemporaryNpcLootGrantTests
{
    [TestMethod]
    public void GrantRandomStartingLoot_ManyNpcs_WandsCarryFullChargeAcrossTheWholeRolledRange()
    {
        const int npcCount = 400;
        var manager = new ComponentManager(initialEntityCapacity: npcCount, initialComponentCapacity: 16);
        new InventoryModule().RegisterComponents(manager);
        var mathUtility = new MathUtility(new Random(7));

        for (var entityId = 0; entityId < npcCount; entityId++)
        {
            TemporaryNpcLootGrant.GrantRandomStartingLoot(manager, entityId, mathUtility);
        }

        var stacks = manager.GetMultiPool<InventoryItemStackComponent>();
        var wandMaxCharges = new HashSet<ushort>();
        for (var denseIndex = 0; denseIndex < stacks.Count; denseIndex++)
        {
            if (stacks.GetReadonlyByDenseIndex(denseIndex).Override?.Activator is WandActivator wand)
            {
                Assert.AreEqual(wand.MaxCharges, wand.Charges);
                wandMaxCharges.Add(wand.MaxCharges);
            }
        }

        CollectionAssert.AreEquivalent(Enumerable.Range(1, 20).Select(static charges => (ushort)charges).ToArray(), wandMaxCharges.ToArray());
    }
}
