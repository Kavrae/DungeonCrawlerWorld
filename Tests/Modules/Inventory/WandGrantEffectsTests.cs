using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Inventory;

[TestClass]
public sealed class WandGrantEffectsTests
{
    private static ComponentManager CreateRegisteredManager()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        new InventoryModule().RegisterComponents(manager);
        manager.RegisterMultiPool<AbilityScoreComponent>();
        return manager;
    }

    private static ItemDefinition CreateBaseWandDefinition() =>
        new(Guid.NewGuid(), "Test Wand", SpriteName: null, Glyph: "w", Color.White, Tags: [], Effects: [],
            Activator: new WandActivator(new TargetingSpec(TargetShape.Burst, Range: 10, AreaSize: 3), new ActionTiming(ActionTimingCategory.Immediate), Charges: 0, MaxCharges: 0));

    [TestMethod]
    public void Grant_RecipientHasIntelligenceScore_BakesInIntelligenceDerivedMaxCharges()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, entityId: 0, AbilityScoreType.Intelligence, baseValue: 300);
        var abilityScores = manager.GetMultiPool<AbilityScoreComponent>();
        var baseDefinition = CreateBaseWandDefinition();

        WandGrantEffects.Grant(manager, abilityScores, entityId: 0, baseDefinition, quantity: 1);

        var stacks = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, entityId: 0, baseDefinition.Id, out var stack));
        var activator = (WandActivator)stack.Override!.Activator!;
        Assert.AreEqual(WandActivationEffects.MaxCharges, activator.Charges);
        Assert.AreEqual(WandActivationEffects.MaxCharges, activator.MaxCharges);
    }

    [TestMethod]
    public void Grant_NoAbilityScoresPool_FallsBackToIntelligenceTotal1()
    {
        var manager = CreateRegisteredManager();
        var baseDefinition = CreateBaseWandDefinition();

        WandGrantEffects.Grant(manager, abilityScores: null, entityId: 0, baseDefinition, quantity: 1);

        var stacks = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, entityId: 0, baseDefinition.Id, out var stack));
        var activator = (WandActivator)stack.Override!.Activator!;
        Assert.AreEqual(WandActivationEffects.MinCharges, activator.MaxCharges);
    }

    [TestMethod]
    public void Grant_Quantity_GrantsThatManyUnitsInOnePlainStack()
    {
        var manager = CreateRegisteredManager();
        var baseDefinition = CreateBaseWandDefinition();

        WandGrantEffects.Grant(manager, abilityScores: null, entityId: 0, baseDefinition, quantity: 10);

        var stacks = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(1, stacks.CountForEntity(0));
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, entityId: 0, baseDefinition.Id, out var stack));
        Assert.AreEqual(10, stack.Quantity);
        Assert.IsFalse(stack.IsDivergent);
    }
}
