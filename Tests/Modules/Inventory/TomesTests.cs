using Engine.ECS.Components;
using Engine.Tiles;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Tags;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Inventory;

[TestClass]
public sealed class TomesTests
{
    private const int HolderEntityId = 4;

    private static ActionDefinition Spell(Guid id) => new(
        id, "Ember Lance", null, "e", Color.OrangeRed, Tags: [GameTags.DamageFire],
        Effects: [new Effect([new DirectDamage(17, 23)])],
        Activator: new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, MaximumRange: 6), new ActionTiming(ActionTimingCategory.Immediate)),
        Description: "A spear of flame.",
        Summary: "Fire damage to one target.");

    [TestMethod]
    public void ATome_IsAOneUseSelfConsumableHoldingOnlyItsSpell()
    {
        var spell = Spell(Guid.NewGuid());

        var tome = Tomes.Create(spell);

        var activator = Assert.IsInstanceOfType<ConsumableActivator>(tome.Activator);
        Assert.IsTrue(activator.ReachesOnlyUser);
        Assert.AreSame(spell, ((SpellGrant)tome.Effects.Single().Entries.Single()).Spell);
        Assert.IsTrue(tome.Tags.Has(GameTags.ItemConsumableTome));
        Assert.IsTrue(tome.CanTrade);
        Assert.AreEqual(Tomes.GoldValue, tome.GoldValue);
        Assert.IsEmpty(tome.ActivationEffects);
    }

    [TestMethod]
    public void ATomesText_NamesTheSpellAndCarriesItsDescription_AndNothingElseOfIt()
    {
        var spell = Spell(Guid.NewGuid());

        var tome = Tomes.Create(spell);

        StringAssert.Contains(tome.Name, spell.Name);
        StringAssert.Contains(tome.Description, spell.Name);
        StringAssert.Contains(tome.Description, spell.Description);
        StringAssert.Contains(tome.Summary, spell.Name);
        foreach (var text in new[] { tome.Name, tome.Description, tome.Summary })
        {
            Assert.DoesNotContain(spell.Summary, text);
            Assert.DoesNotContain("17", text);
            Assert.DoesNotContain("23", text);
        }
    }

    [TestMethod]
    public void ATomesItemId_IsDerivedFromItsSpell()
    {
        var spellId = Guid.NewGuid();

        Assert.AreEqual(Tomes.ItemIdFor(spellId), Tomes.Create(Spell(spellId)).Id);
        Assert.AreEqual(Tomes.ItemIdFor(spellId), Tomes.ItemIdFor(spellId));
        Assert.AreNotEqual(Tomes.ItemIdFor(spellId), Tomes.ItemIdFor(Guid.NewGuid()));
        Assert.AreNotEqual(spellId, Tomes.ItemIdFor(spellId));
    }

    [TestMethod]
    public void TomesOfOneSpell_ShareAStack_AndTomesOfAnotherDont()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 16, initialComponentCapacity: 16));
        var spell = Spell(Guid.NewGuid());

        var first = Tomes.Grant(componentManager, HolderEntityId, spell);
        var second = Tomes.Grant(componentManager, HolderEntityId, spell);
        Tomes.Grant(componentManager, HolderEntityId, Spell(Guid.NewGuid()));

        Assert.AreEqual(first, second);
        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(componentManager.GetMultiPool<InventoryItemStackComponent>(), HolderEntityId, stacks);
        Assert.HasCount(2, stacks);
        var sharedStack = stacks.Single(stack => stack.StackInstanceId == first);
        Assert.AreEqual(2, sharedStack.Quantity);
        Assert.AreEqual(Tomes.ItemIdFor(spell.Id), sharedStack.ItemDefinitionId);
    }

    [TestMethod]
    public void ATome_IsInNoCatalog_AndResolvesThroughItsStack()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 16, initialComponentCapacity: 16));
        var itemCatalog = new ItemCatalog();
        var spell = Spell(Guid.NewGuid());
        var stackInstanceId = Tomes.Grant(componentManager, HolderEntityId, spell);

        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(componentManager.GetMultiPool<InventoryItemStackComponent>(), HolderEntityId, stackInstanceId, out var stack));
        Assert.IsFalse(itemCatalog.TryGet(stack.ItemDefinitionId, out _));
        Assert.IsTrue(InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var resolved));
        Assert.AreEqual($"Tome of {spell.Name}", resolved.Name);
    }

    [TestMethod]
    public void ATome_CountsUnderItsInventoryTab()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 16, initialComponentCapacity: 16));
        Tomes.Grant(componentManager, HolderEntityId, Spell(Guid.NewGuid()));

        var counts = InventoryTagQueries.GetItemCategoryCounts(componentManager, new ItemCatalog(), TestGameplayTags.BuiltIn, HolderEntityId);

        Assert.IsTrue(counts.Any(count => count.Tag == GameTags.ItemConsumableTome && count.Count == 1));
    }
}
