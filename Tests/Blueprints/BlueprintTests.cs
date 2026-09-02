using Engine.Bootstrap;
using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Blueprints;
using Game.Blueprints.Classes;
using Game.Blueprints.NPCs.Generic;
using Game.Blueprints.Objects;
using Game.Blueprints.Races;
using Game.Blueprints.Terrain;
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
    private static short? GetOverrideFlatDamage(in ActionInstanceComponent instance) =>
        instance.Override?.Effects.SelectMany(effect => effect.Entries).OfType<Game.Modules.Actions.Effects.DirectDamage>().First().MinFlatDamage;

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
        statusEffectsModule.Configure(context);

        var containersModule = new ContainersModule();
        containersModule.Configure(context);

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
        ];

        return Bootstrapper.Build(modules, initialEntityCapacity: 100, initialComponentCapacity: 50);
    }

    [TestMethod]
    public void Wall_Build_SetsDisplayTextGlyphAndTransform()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new Wall().Build(ecsContext.ComponentManager, entityId);

        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<DisplayTextComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<GlyphComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));
    }

    [TestMethod]
    public void TreasureChest_Build_SetsDisplayTextGlyphTransformHealthContainerAndImmunities()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new TreasureChest(new MathUtility(new Random(1))).Build(ecsContext.ComponentManager, entityId);

        var displayText = ecsContext.ComponentManager.GetDirectPool<DisplayTextComponent>().GetReadonly(entityId);
        Assert.AreEqual("Treasure Chest", displayText.Name);

        var glyph = ecsContext.ComponentManager.GetDirectPool<GlyphComponent>().GetReadonly(entityId);
        Assert.AreEqual("T", glyph.Glyph);
        Assert.AreEqual(Microsoft.Xna.Framework.Color.Gold, glyph.GlyphColor);

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
        Assert.AreEqual(0, currency.Credits);
    }

    [TestMethod]
    public void Dirt_Build_SetsBackgroundDisplayTextAndTransform()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new Dirt().Build(ecsContext.ComponentManager, entityId);

        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<BackgroundComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<DisplayTextComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));
    }

    [TestMethod]
    public void Grass_Build_SetsBackgroundDisplayTextGlyphAndTransform()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new Grass().Build(ecsContext.ComponentManager, entityId);

        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<BackgroundComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<DisplayTextComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<GlyphComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));
    }

    [TestMethod]
    public void StoneFloor_Build_SetsBackgroundDisplayTextAndTransform()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new StoneFloor().Build(ecsContext.ComponentManager, entityId);

        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<BackgroundComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<DisplayTextComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));
    }

    [TestMethod]
    public void Goblin_Build_SetsRaceBodyPartsMovementActionLockAndTransform()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new Goblin(new MathUtility(new Random(1))).Build(ecsContext.ComponentManager, entityId);

        var racePool = ecsContext.ComponentManager.GetMultiPool<RaceComponent>();
        Assert.IsTrue(racePool.Has(entityId));
        Assert.AreEqual("Goblin", racePool.GetReadonlyByDenseIndex(racePool.GetFirstDenseIndex(entityId)).Name);

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

        var bodyParts = ecsContext.ComponentManager.GetMultiPool<BodyPartComponent>();
        var actualCount = 0;
        var actualMaximumSum = 0f;
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref bodyParts.GetReadonlyByDenseIndex(denseIndex);
            Assert.IsTrue(expectedPartsByName.TryGetValue(part.Name, out var expected), $"Unexpected body part name: {part.Name}");
            Assert.AreEqual(expected.Type, part.Type);
            Assert.AreEqual(expected.IsVital, part.IsVital);
            Assert.AreEqual((float)expected.MaximumHealth, part.MaximumHealth);
            Assert.IsTrue(part.CurrentHealth >= expected.MinimumHealth && part.CurrentHealth <= expected.MaximumHealth);
            actualMaximumSum += part.MaximumHealth;
            actualCount++;
        }

        Assert.AreEqual(expectedPartsByName.Count, actualCount);
        Assert.AreEqual(200f, actualMaximumSum);

        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));

        Assert.IsTrue(ActionInstanceQueries.TryGet(ecsContext.ComponentManager.GetMultiPool<ActionInstanceComponent>(), entityId, PunchAction.Id, out var punch));
        Assert.AreEqual((short)10, GetOverrideFlatDamage(punch));

        AssertHasRandomStartingGold(ecsContext.ComponentManager, entityId);
    }

    [TestMethod]
    public void PlayerBlueprint_Build_SetsGlyphBodyPartsPlayerControlledMovementActionLockAndTransform()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new PlayerBlueprint(new MathUtility(new Random(1)), new UniqueNumberAllocator(new MathUtility(new Random(1)), 1, 13_000_000)).Build(ecsContext.ComponentManager, entityId);

        var glyph = ecsContext.ComponentManager.GetDirectPool<GlyphComponent>().GetReadonly(entityId);
        Assert.AreEqual("@", glyph.Glyph);

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

        var bodyParts = ecsContext.ComponentManager.GetMultiPool<BodyPartComponent>();
        var actualCount = 0;
        var actualMaximumSum = 0f;
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref bodyParts.GetReadonlyByDenseIndex(denseIndex);
            Assert.IsTrue(expectedPartsByName.TryGetValue(part.Name, out var expected), $"Unexpected body part name: {part.Name}");
            Assert.AreEqual(expected.Type, part.Type);
            Assert.AreEqual(expected.IsVital, part.IsVital);
            Assert.AreEqual((float)expected.MaximumHealth, part.MaximumHealth);
            Assert.IsTrue(part.CurrentHealth >= expected.MinimumHealth && part.CurrentHealth <= expected.MaximumHealth);
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

        // Race: Human (via body-part composition), but no ClassComponent -- nothing needs the player to have one.
        var racePool = ecsContext.ComponentManager.GetMultiPool<RaceComponent>();
        Assert.IsTrue(racePool.Has(entityId));
        Assert.AreEqual(Human.RaceId, racePool.GetReadonlyByDenseIndex(racePool.GetFirstDenseIndex(entityId)).Id);
        Assert.IsFalse(ecsContext.ComponentManager.GetMultiPool<ClassComponent>().Has(entityId));

        // The player is always a Crawler.
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<CrawlerComponent>().Has(entityId));

        var abilityInstances = ecsContext.ComponentManager.GetMultiPool<ActionInstanceComponent>();
        // No per-instance Override -- unlike every other race's Punch grant -- so the player's
        // Punch rolls its catalog DirectDamage's own MinFlatDamage..MaxFlatDamage range instead
        // of a fixed number (see ActionInstanceComponent.Override's own doc comment).
        Assert.IsTrue(ActionInstanceQueries.TryGet(abilityInstances, entityId, PunchAction.Id, out var punch));
        Assert.IsNull(punch.Override);
        Assert.IsTrue(ActionInstanceQueries.TryGet(abilityInstances, entityId, MagicMissileAction.Id, out var magicMissile));
        Assert.AreEqual((short)5, GetOverrideFlatDamage(magicMissile));
        Assert.IsTrue(ActionInstanceQueries.TryGet(abilityInstances, entityId, HealAction.Id, out _));
        Assert.IsTrue(ActionInstanceQueries.TryGet(abilityInstances, entityId, ToxicStrikeAction.Id, out _));

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

        AssertHasRandomStartingGold(ecsContext.ComponentManager, entityId);
    }

    [TestMethod]
    public void Fairy_Build_SetsRaceHealthMovementActionLockAndTransform()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new Fairy(new MathUtility(new Random(1))).Build(ecsContext.ComponentManager, entityId);

        Assert.IsTrue(ecsContext.ComponentManager.GetMultiPool<RaceComponent>().Has(entityId));
        var health = ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(entityId);
        Assert.IsTrue(health.CurrentHealth >= 1 && health.CurrentHealth <= health.MaximumHealth);
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));

        Assert.IsTrue(ActionInstanceQueries.TryGet(ecsContext.ComponentManager.GetMultiPool<ActionInstanceComponent>(), entityId, PunchAction.Id, out var punch));
        Assert.AreEqual((short)3, GetOverrideFlatDamage(punch));

        AssertHasRandomStartingGold(ecsContext.ComponentManager, entityId);
    }

    /// <summary>Player/Goblin/Fairy each grant 1-10 starting Gold and 0 Credits via StartingCurrencyGrant.</summary>
    private static void AssertHasRandomStartingGold(ComponentManager componentManager, int entityId)
    {
        var currency = componentManager.GetPackedPool<CurrencyComponent>().GetReadonly(entityId);
        Assert.IsTrue(currency.Gold >= 1 && currency.Gold <= 10, $"Expected Gold in [1,10], was {currency.Gold}.");
        Assert.AreEqual(0, currency.Credits);
    }

    [TestMethod]
    public void Engineer_Build_AppliesCooldownBonusWhenMovementComponentPresent()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Add(entityId, new MovementComponent(MovementMode.Random, null, null));
        ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Add(entityId, new ActionLockComponent(standardLockFrames: 15, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        new Engineer().Build(ecsContext.ComponentManager, entityId);

        var actionLock = ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().GetReadonly(entityId);
        Assert.AreEqual((ushort)13, actionLock.StandardLockFrames); // 15 * 0.9m rounds down to 13.
        Assert.IsTrue(ecsContext.ComponentManager.GetMultiPool<ClassComponent>().Has(entityId));
    }

    [TestMethod]
    public void Engineer_Build_AddsBaselineMovementWhenMovementComponentAbsent()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new Engineer().Build(ecsContext.ComponentManager, entityId);

        // No race ran first, so Engineer merges its own baseline instead of silently doing
        // nothing -- the class still functions when composed (or used) without a race.
        var actionLock = ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().GetReadonly(entityId);
        Assert.AreEqual((ushort)60, actionLock.StandardLockFrames);
        Assert.AreEqual((ushort)0, actionLock.CurrentLockFramesRemaining);
        Assert.IsTrue(ecsContext.ComponentManager.GetMultiPool<ClassComponent>().Has(entityId));
    }

    [TestMethod]
    public void Tank_Build_AddsBaselineHealthWhenHealthComponentAbsent()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new Tank(new MathUtility(new Random(1))).Build(ecsContext.ComponentManager, entityId);

        // No race ran first, so Tank merges its own baseline instead of silently doing
        // nothing -- the class still functions when composed (or used) without a race.
        var health = ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(entityId);
        Assert.AreEqual((ushort)100, health.MaximumHealth);
        Assert.IsTrue(health.CurrentHealth >= 1 && health.CurrentHealth <= health.MaximumHealth);
        Assert.IsTrue(ecsContext.ComponentManager.GetMultiPool<ClassComponent>().Has(entityId));
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

        new Engineer().Build(ecsContext.ComponentManager, entityId);
        new Goblin(mathUtility).Build(ecsContext.ComponentManager, entityId);

        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetMultiPool<ClassComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetMultiPool<RaceComponent>().Has(entityId));
    }

    [TestMethod]
    public void Tank_Build_AppliesHealthBonusWhenHealthComponentPresent()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().Add(entityId, new SimpleHealthComponent(50, 100));

        new Tank(new MathUtility(new Random(1))).Build(ecsContext.ComponentManager, entityId);

        var health = ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(entityId);
        Assert.AreEqual((short)110, health.MaximumHealth);
        AssertHasHealthRegenBonusModifier(ecsContext.ComponentManager, entityId);
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
    /// Regression test for decision #7: Old's GoblinEngineerBlueprint.Build threw, because
    /// Goblin.Build and Engineer.Build both called Add on DisplayTextComponent for the same
    /// entity and DirectComponentPool.Add throws on a second Add. Every blueprint here uses
    /// Merge instead, so this composition must succeed without throwing.
    /// </summary>
    [TestMethod]
    public void GoblinEngineerBlueprint_Build_DoesNotThrow()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        var mathUtility = new MathUtility(new Random(1));

        var blueprint = new GoblinEngineerBlueprint(new Goblin(mathUtility), new Engineer());

        blueprint.Build(ecsContext.ComponentManager, entityId);
    }

    [TestMethod]
    public void GoblinEngineerBlueprint_Build_MergesDisplayTextAcrossTheWholeChain()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        var mathUtility = new MathUtility(new Random(1));

        new GoblinEngineerBlueprint(new Goblin(mathUtility), new Engineer()).Build(ecsContext.ComponentManager, entityId);

        var displayText = ecsContext.ComponentManager.GetDirectPool<DisplayTextComponent>().GetReadonly(entityId);
        // CoreModule's DisplayTextComponent merge lambda concatenates Name with a space and
        // Description with a newline for each stage of the chain (Goblin, then Engineer,
        // then the blueprint's own final merge) -- so all three names/descriptions survive.
        Assert.Contains("Goblin", displayText.Name);
        Assert.Contains("Engineer", displayText.Name);
        Assert.Contains("Goblin Engineer", displayText.Name);
    }

    [TestMethod]
    public void GoblinEngineerBlueprint_Build_AppliesCompoundCooldownReductionOnTopOfEngineersOwn()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        var mathUtility = new MathUtility(new Random(1));

        new GoblinEngineerBlueprint(new Goblin(mathUtility), new Engineer()).Build(ecsContext.ComponentManager, entityId);

        var actionLock = ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().GetReadonly(entityId);
        // Goblin sets a fixed StandardLockFrames of 54; Engineer applies *0.9 and casts to
        // short (54 * 0.9m = 48.6 -> 48), then GoblinEngineerBlueprint applies its own *0.9 to
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
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<RaceComponent>());
        Assert.IsNotNull(ecsContext.ComponentManager.GetMultiPool<RaceComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<ClassComponent>());
        Assert.IsNotNull(ecsContext.ComponentManager.GetMultiPool<ClassComponent>());
    }
}