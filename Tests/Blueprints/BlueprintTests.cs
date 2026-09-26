using Engine.ECS.Entities;
using Engine.Bootstrap;
using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Blueprints;
using Game.Blueprints.Classes;
using Game.Spawning;
using Game.Blueprints.NPCs.Generic;
using Game.Blueprints.Objects;
using Game.Blueprints.Races;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Actions.Definitions.Spells;
using Game.Modules.Class;
using Game.Modules.Class.Components;
using Game.Modules.Containers;
using Game.Modules.Containers.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Crawler;
using Game.Modules.Crawler.Components;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Definitions;
using Game.Modules.Mana;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.Race;
using Game.Modules.Race.Components;
using Game.Modules.Shops;
using Game.Modules.Shops.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Tests.Blueprints;

[TestClass]
public sealed class BlueprintTests
{
    /// <summary>Reads the flat damage a grant's Override pins its DirectDamage entry to (Min == Max, same convention ActionOverrideEffects.OverrideFlatDamage produces) -- null when the instance carries no Override at all.</summary>
    private static short? FlatDamageOf(ActionDefinition action) =>
        action.Effects.SelectMany(effect => effect.Entries).OfType<Game.Modules.Actions.Effects.DirectDamage>().First().MinFlatDamage;

    private static EcsContext BuildEcsContext()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var mathUtility = new MathUtility();
        var context = new GameModuleContext(world, mathUtility, new EventBus()) { EntityMoveSync = new WorldEventSync(world) };

        var movementModule = new MovementModule();
        movementModule.Configure(context);

        var actionsModule = new ActionsModule();
        actionsModule.Configure(context);

        var coreActionsModule = new CoreActionsModule();
        coreActionsModule.Configure(context);

        var processingTierModule = new ProcessingTierModule();
        processingTierModule.Configure(context);

        var coreModule = new CoreModule();
        coreModule.Configure(context);

        var healthModule = new HealthModule();
        healthModule.Configure(context);

        var manaModule = new ManaModule();
        manaModule.Configure(context);

        var statModifiersModule = new StatModifiersModule();
        statModifiersModule.Configure(context);

        var abilityScoresModule = new AbilityScoresModule();
        abilityScoresModule.Configure(context);

        var coreItemsModule = new CoreItemsModule();
        coreItemsModule.Configure(context);

        var statusEffectsModule = new StatusEffectsModule();

        var containersModule = new ContainersModule();
        containersModule.Configure(context);

        var shopModule = new ShopModule();
        shopModule.Configure(context);

        // Registers SpawnRecordComponent, which every build through EntityFactory writes.
        var entityPartsModule = new BlueprintsModule();
        entityPartsModule.Configure(context);

        IReadOnlyList<IModule> modules =
        [
            coreModule,
            healthModule,
            manaModule,
            statModifiersModule,
            abilityScoresModule,
            movementModule,
            new RaceModule(),
            new ClassModule(),
            actionsModule,
            coreActionsModule,
            new CrawlerModule(),
            processingTierModule,
            new InventoryModule(),
            coreItemsModule,
            new CurrencyModule(),
            statusEffectsModule,
            containersModule,
            shopModule,
            entityPartsModule,
        ];

        return Bootstrapper.Build(modules, initialEntityCapacity: 100, initialComponentCapacity: 50, entityKeys: context.EntityKeys);
    }

    [TestMethod]
    public void TreasureChest_Build_IsNamedAndDrawnByItsDefinition_AndSetsTransformHealthContainerAndImmunities()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        ecsContext.BuildDefinition(entityId, TreasureChest.Id);

        Assert.AreEqual("Treasure Chest", ecsContext.NameOf(entityId));
        Assert.IsFalse(ecsContext.ComponentManager.GetPackedPool<DisplayTextComponent>().Has(entityId), "The name is the definition's, not a component written per chest.");

        var appearance = BlueprintTestContext.AppearanceOf(TreasureChest.Id);
        Assert.AreEqual("T", appearance.Glyph);
        Assert.AreEqual(Microsoft.Xna.Framework.Color.Gold, appearance.GlyphColor);
        Assert.IsFalse(ecsContext.ComponentManager.GetPackedPool<GlyphComponent>().Has(entityId));

        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));

        var health = ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(entityId);
        Assert.AreEqual(100f, health.CurrentHealth);
        Assert.AreEqual(100f, health.MaximumHealth);

        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ContainerComponent>().Has(entityId));

        var immunities = ecsContext.ComponentManager.GetMultiPool<StatusEffectImmunityComponent>();
        var immuneTypes = new List<StatusEffectType>();
        for (var denseIndex = immunities.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = immunities.GetNextDenseIndex(denseIndex))
        {
            immuneTypes.Add(immunities.GetReadonlyByDenseIndex(denseIndex).EffectType);
        }
        CollectionAssert.AreEquivalent(new[] { StatusEffectType.Poison, StatusEffectType.Paralysis }, immuneTypes);

        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(ecsContext.ComponentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, stacks);
        var totalItemCount = stacks.Sum(stack => (int)stack.Quantity);
        Assert.IsTrue(stacks.Count >= 1, "Expected at least one starting item stack.");
        Assert.IsTrue(totalItemCount >= 1 && totalItemCount <= 50, $"Expected 1-10 items of quantity 1-5 each (max 50 total), was {totalItemCount}.");

        var currency = ecsContext.ComponentManager.GetPackedPool<CurrencyComponent>().GetReadonly(entityId);
        Assert.IsTrue(currency.Gold >= 0 && currency.Gold <= 5, $"Expected Gold in [0,5], was {currency.Gold}.");
        Assert.IsTrue(currency.Credits >= 0 && currency.Credits <= 1, $"Expected Credits in [0,1], was {currency.Credits}.");
    }

    [TestMethod]
    public void Shop_Build_IsNamedAndDrawnByItsDefinition_AndSetsTransformHealthContainerCurrencyAndImmunities()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        ecsContext.BuildDefinition(entityId, Shop.Id);

        Assert.AreEqual("Shop", ecsContext.NameOf(entityId));

        var appearance = BlueprintTestContext.AppearanceOf(Shop.Id);
        Assert.AreEqual("S", appearance.Glyph);
        Assert.AreEqual(Microsoft.Xna.Framework.Color.DarkBlue, appearance.GlyphColor);
        Assert.AreEqual("Shop-1x1", appearance.SpriteName);

        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));

        var health = ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(entityId);
        Assert.AreEqual(1000f, health.CurrentHealth);
        Assert.AreEqual(1000f, health.MaximumHealth);

        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ContainerComponent>().Has(entityId));
        Assert.IsFalse(ecsContext.ComponentManager.GetPackedPool<ShopComponent>().Has(entityId), "Shop is the shared shell only -- no ShopComponent and no stock by itself.");

        var currency = ecsContext.ComponentManager.GetPackedPool<CurrencyComponent>().GetReadonly(entityId);
        Assert.AreEqual(1000, currency.Gold);
        Assert.AreEqual(0, currency.Credits);

        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(ecsContext.ComponentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, stacks);
        Assert.AreEqual(0, stacks.Count, "Shop by itself grants no stock -- that's each concrete shop type's own composed-in stock part.");

        var immunities = ecsContext.ComponentManager.GetMultiPool<StatusEffectImmunityComponent>();
        var immuneTypes = new List<StatusEffectType>();
        for (var denseIndex = immunities.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = immunities.GetNextDenseIndex(denseIndex))
        {
            immuneTypes.Add(immunities.GetReadonlyByDenseIndex(denseIndex).EffectType);
        }
        CollectionAssert.AreEquivalent(new[] { StatusEffectType.Poison, StatusEffectType.Paralysis }, immuneTypes);
    }

    [TestMethod]
    public void PotionShop_Build_GrantsPotionOnlyShopComponentAndStock()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        ecsContext.BuildDefinition(entityId, PotionShop.Id);

        Assert.AreEqual("Potion Shop", ecsContext.NameOf(entityId), "The composite's own name must replace the shared \"Shop\" shell's, not join onto it.");
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ContainerComponent>().Has(entityId), "PotionShop must still compose in the Shop shell.");

        var shop = ecsContext.ComponentManager.GetPackedPool<ShopComponent>().GetReadonly(entityId);
        CollectionAssert.AreEqual(new[] { Tag.Potion }, shop.AllowedTags?.ToArray());
        Assert.AreEqual(1.10f, shop.BuyMultiplier);
        Assert.AreEqual(0.90f, shop.SellMultiplier);

        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(ecsContext.ComponentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, stacks);
        var totalItemCount = stacks.Sum(stack => (int)stack.Quantity);
        Assert.IsTrue(stacks.Count is >= 5 and <= 10, $"Expected 5-10 stacks, was {stacks.Count}.");
        Assert.IsTrue(totalItemCount >= 5 && totalItemCount <= 50, $"Expected 5-10 items of quantity 1-5 each (max 50 total), was {totalItemCount}.");

        var potionItemIds = new HashSet<Guid>
        {
            HealthPotion.Build().Id, ManaPotion.Build().Id, HotkeyExpansionPotion.Build().Id, DamagePotion.Build().Id,
            ToxicPotion.Build().Id, ToxicIdol.Build().Id, ImmunityTestPotion.Build().Id, ResistanceTestPotion.Build().Id,
        };
        foreach (var stack in stacks)
        {
            Assert.IsTrue(potionItemIds.Contains(stack.ItemDefinitionId), "Every PotionShop stack must be one of the catalog's Potion-tagged items.");
        }
    }

    [TestMethod]
    public void GeneralShop_Build_GrantsAnyTagShopComponentAndStock()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        ecsContext.BuildDefinition(entityId, GeneralShop.Id);

        Assert.AreEqual("General Shop", ecsContext.NameOf(entityId), "The composite's own name must replace the shared \"Shop\" shell's, not join onto it.");
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ContainerComponent>().Has(entityId), "GeneralShop must still compose in the Shop shell.");

        var shop = ecsContext.ComponentManager.GetPackedPool<ShopComponent>().GetReadonly(entityId);
        Assert.IsNull(shop.AllowedTags, "General Shop must trade any tag.");
        Assert.AreEqual(1.20f, shop.BuyMultiplier);
        Assert.AreEqual(0.80f, shop.SellMultiplier);

        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(ecsContext.ComponentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, stacks);
        var totalItemCount = stacks.Sum(stack => (int)stack.Quantity);
        Assert.IsTrue(stacks.Count is >= 5 and <= 10, $"Expected 5-10 stacks, was {stacks.Count}.");
        Assert.IsTrue(totalItemCount >= 5 && totalItemCount <= 50, $"Expected 5-10 items of quantity 1-5 each (max 50 total), was {totalItemCount}.");
    }

    [TestMethod]
    public void Goblin_Build_SetsRaceBodyPartsMovementActionLockAndTransform()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        ecsContext.BuildDefinition(entityId, Goblin.Id);

        var racePool = ecsContext.ComponentManager.GetPackedPool<RaceSlotsComponent>();
        Assert.IsTrue(racePool.Has(entityId));
        Assert.AreEqual(BlueprintTestContext.Definitions.Races.GetId(Goblin.Id), racePool.GetReadonly(entityId).Primary);

        Assert.IsFalse(ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().Has(entityId));

        var expectedPartsByName = new Dictionary<string, (BodyPartType Type, ushort MinimumHealth, ushort MaximumHealth, bool IsVital)>
        {
            ["Head"] = (BodyPartType.Head, 30, 30, true),
            ["Torso"] = (BodyPartType.Torso, 50, 50, true),
            ["Internal"] = (BodyPartType.Internal, 10, 10, true),
            ["Left Arm"] = (BodyPartType.Arm, 15, 15, false),
            ["Right Arm"] = (BodyPartType.Arm, 15, 15, false),
            ["Left Hand"] = (BodyPartType.Hand, 5, 5, false),
            ["Right Hand"] = (BodyPartType.Hand, 5, 5, false),
            ["Left Leg"] = (BodyPartType.Leg, 25, 25, false),
            ["Right Leg"] = (BodyPartType.Leg, 25, 25, false),
            ["Left Foot"] = (BodyPartType.Foot, 10, 10, false),
            ["Right Foot"] = (BodyPartType.Foot, 10, 10, false),
        };

        var bodyParts = EntityBodyParts.For(ecsContext.ComponentManager, BlueprintTestContext.Definitions);
        var actualCount = 0;
        var actualMaximumSum = 0f;
        foreach (var part in bodyParts.Parts(entityId))
        {
            Assert.IsTrue(expectedPartsByName.TryGetValue(part.Name, out var expected), $"Unexpected body part name: {part.Name}");
            Assert.AreEqual(expected.Type, part.Type);
            Assert.AreEqual(expected.IsVital, part.IsVital);
            Assert.AreEqual((float)expected.MaximumHealth, part.MaximumHealth);
            Assert.AreEqual((float)expected.MaximumHealth, part.CurrentHealth, "A Goblin carries no MaximumHealth modifier, so it starts at its stored maximum exactly.");
            actualMaximumSum += part.MaximumHealth;
            actualCount++;
        }

        Assert.AreEqual(expectedPartsByName.Count, actualCount);
        Assert.AreEqual(200f, actualMaximumSum);

        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));

        Assert.IsTrue(ecsContext.ActionsOf().TryGetEffectiveAction(entityId, QuickAttackAction.Id, out var punch));
        Assert.AreEqual((short)10, FlatDamageOf(punch));

        AssertHasRandomStartingGoldAndCredits(ecsContext.ComponentManager, entityId);
    }

    [TestMethod]
    public void PlayerBlueprint_Build_IsNamedAndDrawnAsThePlayer_AndSetsBodyPartsPlayerControlledMovementActionLockAndTransform()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        ecsContext.BuildBlueprint(entityId, BlueprintTestContext.PlayerBlueprint);

        Assert.AreEqual("Player1", ecsContext.NameOf(entityId), "PlayerKit's explicit name replaces the one Human and Tank would compose.");
        var appearance = BlueprintTestContext.Definitions.Resolve(BlueprintTestContext.PlayerBlueprint).Appearance;
        Assert.AreEqual("@", appearance.Glyph);
        Assert.AreEqual("Player", appearance.SpriteName);

        // The player is Complex health via the Human race it composes in -- no SimpleHealthComponent at all.
        Assert.IsFalse(ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().Has(entityId));

        var expectedPartsByName = new Dictionary<string, (BodyPartType Type, ushort MinimumHealth, ushort MaximumHealth, bool IsVital)>
        {
            ["Head"] = (BodyPartType.Head, 40, 40, true),
            ["Torso"] = (BodyPartType.Torso, 65, 65, true),
            ["Internal"] = (BodyPartType.Internal, 15, 15, true),
            ["Left Arm"] = (BodyPartType.Arm, 20, 20, false),
            ["Right Arm"] = (BodyPartType.Arm, 20, 20, false),
            ["Left Hand"] = (BodyPartType.Hand, 5, 5, false),
            ["Right Hand"] = (BodyPartType.Hand, 5, 5, false),
            ["Left Leg"] = (BodyPartType.Leg, 30, 30, false),
            ["Right Leg"] = (BodyPartType.Leg, 30, 30, false),
            ["Left Foot"] = (BodyPartType.Foot, 10, 10, false),
            ["Right Foot"] = (BodyPartType.Foot, 10, 10, false),
        };

        var bodyParts = EntityBodyParts.For(ecsContext.ComponentManager, BlueprintTestContext.Definitions);
        var actualCount = 0;
        var actualMaximumSum = 0f;
        foreach (var part in bodyParts.Parts(entityId))
        {
            Assert.IsTrue(expectedPartsByName.TryGetValue(part.Name, out var expected), $"Unexpected body part name: {part.Name}");
            Assert.AreEqual(expected.Type, part.Type);
            Assert.AreEqual(expected.IsVital, part.IsVital);
            Assert.AreEqual((float)expected.MaximumHealth, part.MaximumHealth);
            // At or above the stored maximum: the player's MaximumHealth modifiers raise the cap above
            // it, and the entity is built full against that raised cap. The exact value is
            // PlayerRecipe_Build_StartsEveryBodyPartAtItsEffectiveMaximum's business.
            Assert.IsTrue(part.CurrentHealth >= expected.MaximumHealth);
            actualMaximumSum += part.MaximumHealth;
            actualCount++;
        }

        Assert.AreEqual(expectedPartsByName.Count, actualCount);
        Assert.AreEqual(250f, actualMaximumSum);

        var movement = ecsContext.ComponentManager.GetPackedPool<MovementComponent>().GetReadonly(entityId);
        Assert.AreEqual(MovementMode.PlayerControlled, movement.MovementMode);
        Assert.IsNull(movement.NextMapPosition);

        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));

        // Race: Human, class: Tank -- both composed in, each filling its own slot.
        Assert.AreEqual(BlueprintTestContext.Definitions.Races.GetId(Human.Id), ecsContext.ComponentManager.GetPackedPool<RaceSlotsComponent>().GetReadonly(entityId).Primary);
        Assert.AreEqual(BlueprintTestContext.Definitions.Classes.GetId(Tank.Id), ecsContext.ComponentManager.GetPackedPool<ClassSlotsComponent>().GetReadonly(entityId).Primary);
        AssertHasHealthRegenBonusModifier(ecsContext.ComponentManager, entityId);

        // Tank took its Complex-health path: a +10% MaximumHealth modifier on top of Human's body
        // parts, rather than a SimpleHealthComponent that would have taken those parts out of play.
        Assert.IsTrue(HasMaximumHealthBonusModifier(ecsContext.ComponentManager, entityId, 0.10f));

        // No per-instance Override -- unlike every other race's QuickAttack grant -- so the
        // player's QuickAttack rolls its catalog DirectDamage's own MinFlatDamage..MaxFlatDamage
        // range instead of a fixed number (see ActionInstanceComponent.Override's own doc comment).
        Assert.IsTrue(ecsContext.ActionsOf().TryGetEffectiveAction(entityId, QuickAttackAction.Id, out var punch));
        BlueprintTestContext.Actions.TryGet(QuickAttackAction.Id, out var catalogQuickAttack);
        Assert.AreEqual(FlatDamageOf(catalogQuickAttack!), FlatDamageOf(punch), "No override: the player's QuickAttack is the catalog definition, rolling its own damage range.");
        Assert.IsTrue(ecsContext.ActionsOf().TryGetEffectiveAction(entityId, MagicMissileAction.Id, out var magicMissile));
        Assert.AreEqual((short)5, FlatDamageOf(magicMissile));
        Assert.IsTrue(ecsContext.ActionsOf().Has(entityId, HealAction.Id));
        Assert.IsTrue(ecsContext.ActionsOf().Has(entityId, ToxicStrikeAction.Id));

        // Starting items: 5 Health Potions, 5 Mana Potions, 3 Hotkey Expansion Potions, 5 Volatile
        // Concoctions (damage), 5 Toxic Flasks (Poison+Burning), 5 Toxic Idols (Poison aura toggle),
        // 5 Scrolls of Healing, 5 Scrolls of Torch, 5 Vials of Warding (Burning+Poison immunity),
        // 5 Draughts of Insulation (Burning+Poison resistance) -- see the ActionEffect/
        // ActionActivator plan's concrete test content. Plus a batch of 10 Wands of Fireball and
        // one TEMPORARY divergent Adjacent-targeting test wand -- two separate stacks sharing
        // WandOfFireball.Id, since the divergent one carries its own Override -- see the per-slot
        // item divergence work.
        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(ecsContext.ComponentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, stacks);
        Assert.HasCount(12, stacks);

        var healthPotionStack = stacks.Single(stack => stack.ItemDefinitionId == HealthPotion.Id);
        Assert.AreEqual(5, healthPotionStack.Quantity);
        Assert.IsFalse(healthPotionStack.IsDisabled);

        var manaPotionStack = stacks.Single(stack => stack.ItemDefinitionId == ManaPotion.Id);
        Assert.AreEqual(5, manaPotionStack.Quantity);
        Assert.IsFalse(manaPotionStack.IsDisabled);

        var hotkeyExpansionPotionStack = stacks.Single(stack => stack.ItemDefinitionId == HotkeyExpansionPotion.Id);
        Assert.AreEqual(3, hotkeyExpansionPotionStack.Quantity);
        Assert.IsFalse(hotkeyExpansionPotionStack.IsDisabled);

        var damagePotionStack = stacks.Single(stack => stack.ItemDefinitionId == DamagePotion.Id);
        Assert.AreEqual(5, damagePotionStack.Quantity);
        Assert.IsFalse(damagePotionStack.IsDisabled);

        var toxicPotionStack = stacks.Single(stack => stack.ItemDefinitionId == ToxicPotion.Id);
        Assert.AreEqual(5, toxicPotionStack.Quantity);
        Assert.IsFalse(toxicPotionStack.IsDisabled);

        var toxicIdolStack = stacks.Single(stack => stack.ItemDefinitionId == ToxicIdol.Id);
        Assert.AreEqual(5, toxicIdolStack.Quantity);
        Assert.IsFalse(toxicIdolStack.IsDisabled);

        var scrollOfHealingStack = stacks.Single(stack => stack.ItemDefinitionId == ScrollOfHealing.Id);
        Assert.AreEqual(5, scrollOfHealingStack.Quantity);
        Assert.IsFalse(scrollOfHealingStack.IsDisabled);

        var scrollOfTorchStack = stacks.Single(stack => stack.ItemDefinitionId == ScrollOfTorch.Id);
        Assert.AreEqual(5, scrollOfTorchStack.Quantity);
        Assert.IsFalse(scrollOfTorchStack.IsDisabled);

        var immunityTestPotionStack = stacks.Single(stack => stack.ItemDefinitionId == ImmunityTestPotion.Id);
        Assert.AreEqual(5, immunityTestPotionStack.Quantity);
        Assert.IsFalse(immunityTestPotionStack.IsDisabled);

        var resistanceTestPotionStack = stacks.Single(stack => stack.ItemDefinitionId == ResistanceTestPotion.Id);
        Assert.AreEqual(5, resistanceTestPotionStack.Quantity);
        Assert.IsFalse(resistanceTestPotionStack.IsDisabled);

        var wandOfFireballStacks = stacks.Where(stack => stack.ItemDefinitionId == WandOfFireball.Id).ToList();
        Assert.HasCount(2, wandOfFireballStacks);

        var plainWandStack = wandOfFireballStacks.Single(stack => !stack.IsDivergent);
        Assert.AreEqual(10, plainWandStack.Quantity);
        Assert.IsFalse(plainWandStack.IsDisabled);
        Assert.IsNotNull(plainWandStack.Override);
        Assert.IsInstanceOfType<WandActivator>(plainWandStack.Override!.Activator);

        var divergentWandStack = wandOfFireballStacks.Single(stack => stack.IsDivergent);
        Assert.AreEqual(1, divergentWandStack.Quantity);
        var divergentWandActivator = (WandActivator)divergentWandStack.Override!.Activator!;
        Assert.AreEqual(TargetShape.Adjacent, divergentWandActivator.Targeting.Shape);

        var hotkeyExpansionUnlock = ecsContext.ComponentManager.GetPackedPool<HotkeyExpansionUnlockComponent>().GetReadonly(entityId);
        Assert.AreEqual((short)5, hotkeyExpansionUnlock.UnlockedSlotCount);

        AssertHasFixedStartingGold(ecsContext.ComponentManager, entityId);
    }

    [TestMethod]
    public void Fairy_Build_SetsRaceHealthMovementActionLockAndTransform()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        ecsContext.BuildDefinition(entityId, Fairy.Id);

        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<RaceSlotsComponent>().Has(entityId));
        var health = ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(entityId);
        Assert.AreEqual(health.MaximumHealth, health.CurrentHealth);
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));

        Assert.IsTrue(ecsContext.ActionsOf().TryGetEffectiveAction(entityId, QuickAttackAction.Id, out var punch));
        Assert.AreEqual((short)3, FlatDamageOf(punch));

        AssertHasRandomStartingGoldAndCredits(ecsContext.ComponentManager, entityId);
    }

    /// <summary>Player grants a flat 100 starting Gold and 0 Credits via StartingCurrencyGrant.GrantFixedStartingGold.</summary>
    private static void AssertHasFixedStartingGold(ComponentManager componentManager, int entityId)
    {
        var currency = componentManager.GetPackedPool<CurrencyComponent>().GetReadonly(entityId);
        Assert.AreEqual(StartingCurrencyGrant.PlayerStartingGold, currency.Gold);
        Assert.AreEqual(0, currency.Credits);
    }

    /// <summary>Goblin/Fairy grant 1-10 starting Gold and 0-1 Credits via StartingCurrencyGrant.GrantRandomStartingGoldAndCredits.</summary>
    private static void AssertHasRandomStartingGoldAndCredits(ComponentManager componentManager, int entityId)
    {
        var currency = componentManager.GetPackedPool<CurrencyComponent>().GetReadonly(entityId);
        Assert.IsTrue(currency.Gold >= 1 && currency.Gold <= 10, $"Expected Gold in [1,10], was {currency.Gold}.");
        Assert.IsTrue(currency.Credits >= 0 && currency.Credits <= 1, $"Expected Credits in [0,1], was {currency.Credits}.");
    }

    [TestMethod]
    public void Engineer_Build_AppliesCooldownBonusWhenMovementComponentPresent()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Add(entityId, new MovementComponent(MovementMode.Random, null, null));
        ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Add(entityId, new ActionLockComponent(standardLockFrames: 15, currentLockTotalFrames: 0, unlockedAtFrame: 0));

        ecsContext.BuildDefinition(entityId, Engineer.Id);

        var actionLock = ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().GetReadonly(entityId);
        Assert.AreEqual((ushort)13, actionLock.StandardLockFrames); // 15 * 0.9m rounds down to 13.
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ClassSlotsComponent>().Has(entityId));
    }

    [TestMethod]
    public void Engineer_Build_AddsBaselineMovementWhenMovementComponentAbsent()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        ecsContext.BuildDefinition(entityId, Engineer.Id);

        // No race ran first, so Engineer merges its own baseline instead of silently doing
        // nothing -- the class still functions when composed (or used) without a race.
        var actionLock = ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().GetReadonly(entityId);
        Assert.AreEqual((ushort)60, actionLock.StandardLockFrames);
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ClassSlotsComponent>().Has(entityId));
    }

    [TestMethod]
    public void Tank_Build_AddsBaselineHealthWhenHealthComponentAbsent()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        ecsContext.BuildDefinition(entityId, Tank.Id);

        // No race ran first, so Tank merges its own baseline instead of silently doing
        // nothing -- the class still functions when composed (or used) without a race.
        var health = ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(entityId);
        Assert.AreEqual(100f, health.MaximumHealth, "The stored baseline, which the bonus modifier scales rather than rewrites.");
        Assert.AreEqual(110f, health.CurrentHealth, 0.001f, "Built full against the bonus-effective maximum.");
        Assert.IsTrue(HasMaximumHealthBonusModifier(ecsContext.ComponentManager, entityId, 0.10f));
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ClassSlotsComponent>().Has(entityId));
        AssertHasHealthRegenBonusModifier(ecsContext.ComponentManager, entityId);
    }

    /// <summary>
    /// Engineer and Goblin are each independently order-independent: composing them in
    /// reverse (class before race) never throws or drops the class's mechanic -- Engineer
    /// merges its own baseline MovementComponent/ActionLockComponent since neither exists yet,
    /// then Goblin's own MovementComponent merges on top via MovementModule's registered merge
    /// action. The exact resulting numbers depend on order, but the entity always ends up with
    /// a working MovementComponent/ActionLockComponent either way.
    /// </summary>
    [TestMethod]
    public void EngineerThenGoblin_ComposedInReverseOrder_StillProducesAWorkingEntity()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        var mathUtility = new MathUtility(new Random(1));

        ecsContext.BuildDefinition(entityId, Engineer.Id);
        ecsContext.BuildDefinition(entityId, Goblin.Id);

        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ClassSlotsComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<RaceSlotsComponent>().Has(entityId));
    }

    [TestMethod]
    public void Tank_Build_AppliesHealthBonusWhenHealthComponentPresent()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().Add(entityId, new SimpleHealthComponent(50, 100));

        ecsContext.BuildDefinition(entityId, Tank.Id);

        // The bonus is a modifier, not a rewrite: the race's own stored maximum is untouched and
        // every reader scales it through the modifier instead (see StatModifierComponent).
        var health = ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(entityId);
        Assert.AreEqual(100f, health.MaximumHealth);
        Assert.IsTrue(HasMaximumHealthBonusModifier(ecsContext.ComponentManager, entityId, 0.10f));
        Assert.IsTrue(HealthQueries.TryGetEffectiveMaximum(
            ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>(),
            EntityBodyParts.For(ecsContext.ComponentManager, BlueprintTestContext.Definitions),
            ecsContext.ComponentManager.GetMultiPool<StatModifierComponent>(),
            entityId,
            out var effectiveMaximum));
        Assert.AreEqual(110f, effectiveMaximum, 0.001f);
        Assert.AreEqual(60f, health.CurrentHealth, 0.001f, "50 short of 100 before, 50 short of 110 after.");
        AssertHasHealthRegenBonusModifier(ecsContext.ComponentManager, entityId);
    }

    /// <summary>A Complex-health entity has no MaximumHealth field for Tank to multiply in place, so its health bonus arrives as a StatModifier -- and critically not as a SimpleHealthComponent, which HealthDamage would dispatch on ahead of the entity's body parts.</summary>
    [TestMethod]
    public void Tank_Build_AppliesMaximumHealthModifierWhenBodyPartsPresent()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        ecsContext.BuildDefinition(entityId, Human.Id);

        ecsContext.BuildDefinition(entityId, Tank.Id);

        Assert.IsFalse(ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().Has(entityId));
        Assert.IsTrue(EntityBodyParts.For(ecsContext.ComponentManager, BlueprintTestContext.Definitions).Has(entityId));
        Assert.IsTrue(HasMaximumHealthBonusModifier(ecsContext.ComponentManager, entityId, 0.10f));
        AssertHasHealthRegenBonusModifier(ecsContext.ComponentManager, entityId);

        Assert.IsTrue(HealthQueries.TryGetEffectiveMaximum(
            ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>(),
            EntityBodyParts.For(ecsContext.ComponentManager, BlueprintTestContext.Definitions),
            ecsContext.ComponentManager.GetMultiPool<StatModifierComponent>(),
            entityId,
            out var effectiveMaximum));
        Assert.AreEqual(250f * 1.10f, effectiveMaximum, 0.001f);
    }

    private static bool HasMaximumHealthBonusModifier(ComponentManager componentManager, int entityId, float magnitude)
    {
        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();
        for (var denseIndex = statModifiers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = statModifiers.GetNextDenseIndex(denseIndex))
        {
            var modifier = statModifiers.GetReadonlyByDenseIndex(denseIndex);
            if (modifier.Target == StatModifierTarget.MaximumHealth && modifier.Operation == StatModifierOperation.Multiplicative && modifier.Magnitude == magnitude)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Regen has no stored field for Tank to have multiplied in place anymore (see SimpleHealthRegenSystem) -- its +10% bonus is a granted StatModifier instead, asserted here by presence/shape rather than by reading a SimpleHealthComponent field.</summary>
    private static void AssertHasHealthRegenBonusModifier(ComponentManager componentManager, int entityId)
    {
        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();
        for (var denseIndex = statModifiers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = statModifiers.GetNextDenseIndex(denseIndex))
        {
            var modifier = statModifiers.GetReadonlyByDenseIndex(denseIndex);
            if (modifier.Target == StatModifierTarget.HealthRegen && modifier.Operation == StatModifierOperation.Multiplicative && modifier.Magnitude == 0.10f)
            {
                return;
            }
        }

        Assert.Fail("Expected a permanent +10% HealthRegen StatModifier granted by Tank.");
    }

    /// <summary>
    /// Regression test for decision #7: Old's GoblinEngineerPart.Build threw, because
    /// Goblin.Build and Engineer.Build both called Add on DisplayTextComponent for the same
    /// entity and DirectComponentPool.Add throws on a second Add. Every blueprint here uses
    /// Merge instead, so this composition must succeed without throwing.
    /// </summary>
    [TestMethod]
    public void GoblinEngineerRecipe_Build_DoesNotThrow()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        var mathUtility = new MathUtility(new Random(1));

        ecsContext.BuildBlueprint(entityId, BlueprintTestContext.GoblinEngineerBlueprint);
    }

    [TestMethod]
    public void GoblinEngineer_IsNamedByGoblinAndEngineer_AndDescribedByItself()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        ecsContext.BuildBlueprint(entityId, BlueprintTestContext.GoblinEngineerBlueprint);

        StringAssert.EndsWith(ecsContext.NameOf(entityId), " Goblin Engineer", "A goblin's seeded name followed by the Engineer class's.");
        StringAssert.StartsWith(BlueprintTestContext.AppearanceOf(Game.Blueprints.Composites.GoblinEngineer.Id).Description, "Engineers. The incels of the goblin world.");
        Assert.IsFalse(ecsContext.ComponentManager.GetPackedPool<DisplayTextComponent>().Has(entityId));
    }

    [TestMethod]
    public void GoblinEngineerRecipe_Build_AppliesCompoundCooldownReductionOnTopOfEngineersOwn()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        var mathUtility = new MathUtility(new Random(1));

        ecsContext.BuildBlueprint(entityId, BlueprintTestContext.GoblinEngineerBlueprint);

        var actionLock = ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().GetReadonly(entityId);
        // Goblin sets a fixed StandardLockFrames of 54; Engineer applies *0.9 and casts to
        // short (54 * 0.9m = 48.6 -> 48), then GoblinEngineerPart applies its own *0.9 to
        // that already-truncated value and casts again (48 * 0.9m = 43.2 -> 43) -- each stage
        // rounds down independently, not one combined multiplication.
        Assert.AreEqual((ushort)43, actionLock.StandardLockFrames);
    }

    [TestMethod]
    public void RaceAndClassModules_Register_AsMultiComponentPools()
    {
        var ecsContext = BuildEcsContext();

        // GetMultiPool<T> itself throws unless the registered pool is actually a
        // MultiComponentPool<T> -- reaching the assertions below is the proof.
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<RaceSlotsComponent>());
        Assert.IsNotNull(ecsContext.ComponentManager.GetPackedPool<RaceSlotsComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<ClassSlotsComponent>());
        Assert.IsNotNull(ecsContext.ComponentManager.GetPackedPool<ClassSlotsComponent>());
    }

    /// <summary>
    /// Every entity is built at full health, and "full" means the modifier-effective maximum, not
    /// the stored one. The player is the case that catches a regression here: Human grants its
    /// parts at their stored maximum, then Tank's +10% and the player's own +50% raise the cap
    /// above it, so without the top-up the player spawns at 62.5%.
    /// </summary>
    [TestMethod]
    public void PlayerRecipe_Build_StartsEveryBodyPartAtItsEffectiveMaximum()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        ecsContext.BuildBlueprint(entityId, BlueprintTestContext.PlayerBlueprint);

        var bodyParts = EntityBodyParts.For(ecsContext.ComponentManager, BlueprintTestContext.Definitions);
        var statModifiers = ecsContext.ComponentManager.GetMultiPool<StatModifierComponent>();
        var partCount = 0;
        foreach (var part in bodyParts.Parts(entityId))
        {
            var effectiveMaximum = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, part.MaximumHealth);

            Assert.AreEqual(effectiveMaximum, part.CurrentHealth, 0.001f, $"{part.Name} did not start at its effective maximum.");
            Assert.IsTrue(effectiveMaximum > part.MaximumHealth, $"{part.Name}'s effective maximum should exceed its stored one -- otherwise this test proves nothing.");
            partCount++;
        }

        Assert.IsTrue(partCount > 0, "Expected the player to have body parts.");
    }

    /// <summary>The Complex branch of Tank's own bonus, without the player's extra modifier on top.</summary>
    [TestMethod]
    public void Tank_Build_OnComplexEntity_StartsEveryBodyPartAtItsEffectiveMaximum()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        ecsContext.BuildDefinition(entityId, Human.Id);
        ecsContext.BuildDefinition(entityId, Tank.Id);

        var bodyParts = EntityBodyParts.For(ecsContext.ComponentManager, BlueprintTestContext.Definitions);
        var statModifiers = ecsContext.ComponentManager.GetMultiPool<StatModifierComponent>();
        foreach (var part in bodyParts.Parts(entityId))
        {
            var effectiveMaximum = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, part.MaximumHealth);

            Assert.AreEqual(effectiveMaximum, part.CurrentHealth, 0.001f, $"{part.Name} did not start at its effective maximum.");
        }
    }

    /// <summary>Tank's Simple branch raises the cap in place, so current health has to move with it.</summary>
    [TestMethod]
    public void Tank_Build_OnSimpleEntity_StartsAtItsRaisedMaximum()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        ecsContext.BuildDefinition(entityId, Fairy.Id);
        ecsContext.BuildDefinition(entityId, Tank.Id);

        var health = ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(entityId);

        Assert.AreEqual(100f, health.MaximumHealth, 0.001f, "Fairy's own maximum, left as it authored it.");
        Assert.AreEqual(110f, health.CurrentHealth, 0.001f, "Full against the bonus-effective maximum.");
        Assert.IsTrue(HasMaximumHealthBonusModifier(ecsContext.ComponentManager, entityId, 0.10f));
    }
}