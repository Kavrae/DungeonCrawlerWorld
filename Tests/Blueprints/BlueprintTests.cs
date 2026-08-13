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
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Actions.Definitions.Spells;
using Game.Modules.Class;
using Game.Modules.Class.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Crawler;
using Game.Modules.Crawler.Components;
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
using Game.World;

namespace Tests.Blueprints;

[TestClass]
public sealed class BlueprintTests
{
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
    public void Goblin_Build_SetsRaceHealthMovementActionLockAndTransform()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new Goblin(new MathUtility(new Random(1))).Build(ecsContext.ComponentManager, entityId);

        var racePool = ecsContext.ComponentManager.GetMultiPool<RaceComponent>();
        Assert.IsTrue(racePool.Has(entityId));
        Assert.AreEqual("Goblin", racePool.GetReadonlyByDenseIndex(racePool.GetFirstDenseIndex(entityId)).Name);
        var health = ecsContext.ComponentManager.GetPackedPool<HealthComponent>().GetReadonly(entityId);
        Assert.IsTrue(health.CurrentHealth >= 1 && health.CurrentHealth <= health.MaximumHealth);
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));

        Assert.IsTrue(ActionInstanceQueries.TryGet(ecsContext.ComponentManager.GetMultiPool<ActionInstanceComponent>(), entityId, PunchAction.Id, out var punch));
        Assert.AreEqual((short)10, punch.DamageAmount);
    }

    [TestMethod]
    public void PlayerBlueprint_Build_SetsGlyphHealthPlayerControlledMovementActionLockAndTransform()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new PlayerBlueprint(new MathUtility(new Random(1)), new UniqueNumberAllocator(new MathUtility(new Random(1)), 1, 13_000_000)).Build(ecsContext.ComponentManager, entityId);

        var glyph = ecsContext.ComponentManager.GetDirectPool<GlyphComponent>().GetReadonly(entityId);
        Assert.AreEqual("@", glyph.Glyph);

        var health = ecsContext.ComponentManager.GetPackedPool<HealthComponent>().GetReadonly(entityId);
        Assert.IsTrue(health.CurrentHealth >= 1 && health.CurrentHealth <= health.MaximumHealth);

        var movement = ecsContext.ComponentManager.GetPackedPool<MovementComponent>().GetReadonly(entityId);
        Assert.AreEqual(MovementMode.PlayerControlled, movement.MovementMode);
        Assert.IsNull(movement.NextMapPosition);

        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));

        // No RaceComponent/ClassComponent -- nothing needs the player to have either.
        Assert.IsFalse(ecsContext.ComponentManager.GetMultiPool<RaceComponent>().Has(entityId));
        Assert.IsFalse(ecsContext.ComponentManager.GetMultiPool<ClassComponent>().Has(entityId));

        // The player is always a Crawler.
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<CrawlerComponent>().Has(entityId));

        var abilityInstances = ecsContext.ComponentManager.GetMultiPool<ActionInstanceComponent>();
        // DamageAmount 0 -- no per-instance override, unlike every other race's Punch grant -- so the
        // player's Punch rolls its catalog DirectDamage's own MinAmount..MaxAmount range instead
        // of a fixed number (see ActionEffectContext.DamageOverride's own doc comment).
        Assert.IsTrue(ActionInstanceQueries.TryGet(abilityInstances, entityId, PunchAction.Id, out var punch));
        Assert.AreEqual((short)0, punch.DamageAmount);
        Assert.IsTrue(ActionInstanceQueries.TryGet(abilityInstances, entityId, MagicMissileAction.Id, out var magicMissile));
        Assert.AreEqual((short)5, magicMissile.DamageAmount);
        Assert.IsTrue(ActionInstanceQueries.TryGet(abilityInstances, entityId, HealAction.Id, out _));
        Assert.IsTrue(ActionInstanceQueries.TryGet(abilityInstances, entityId, ToxicStrikeAction.Id, out _));

        // Starting items: 5 Health Potions, 5 Mana Potions, 3 Hotkey Expansion Potions, 5 Volatile
        // Concoctions (damage), 5 Toxic Flasks (Poison+Burning), 5 Toxic Idols (Poison aura toggle),
        // 5 Scrolls of Healing, 5 Scrolls of Torch -- see the ActionEffect/ActionActivator plan's
        // concrete test content.
        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(ecsContext.ComponentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, stacks);
        Assert.HasCount(8, stacks);

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

        var hotkeyExpansionUnlock = ecsContext.ComponentManager.GetPackedPool<HotkeyExpansionUnlockComponent>().GetReadonly(entityId);
        Assert.AreEqual((short)5, hotkeyExpansionUnlock.UnlockedSlotCount);
    }

    [TestMethod]
    public void Fairy_Build_SetsRaceHealthMovementActionLockAndTransform()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new Fairy(new MathUtility(new Random(1))).Build(ecsContext.ComponentManager, entityId);

        Assert.IsTrue(ecsContext.ComponentManager.GetMultiPool<RaceComponent>().Has(entityId));
        var health = ecsContext.ComponentManager.GetPackedPool<HealthComponent>().GetReadonly(entityId);
        Assert.IsTrue(health.CurrentHealth >= 1 && health.CurrentHealth <= health.MaximumHealth);
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));

        Assert.IsTrue(ActionInstanceQueries.TryGet(ecsContext.ComponentManager.GetMultiPool<ActionInstanceComponent>(), entityId, PunchAction.Id, out var punch));
        Assert.AreEqual((short)5, punch.DamageAmount);
    }

    [TestMethod]
    public void Engineer_Build_AppliesCooldownBonusWhenMovementComponentPresent()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Add(entityId, new MovementComponent(MovementMode.Random, 15, null, null));

        new Engineer().Build(ecsContext.ComponentManager, entityId);

        var movement = ecsContext.ComponentManager.GetPackedPool<MovementComponent>().GetReadonly(entityId);
        Assert.AreEqual((short)13, movement.ActionCooldownFrames); // 15 * 0.9m rounds down to 13.
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
        var movement = ecsContext.ComponentManager.GetPackedPool<MovementComponent>().GetReadonly(entityId);
        Assert.AreEqual((short)60, movement.ActionCooldownFrames);
        var actionLock = ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().GetReadonly(entityId);
        Assert.AreEqual((short)0, actionLock.LockFramesRemaining);
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
        var health = ecsContext.ComponentManager.GetPackedPool<HealthComponent>().GetReadonly(entityId);
        Assert.AreEqual((short)100, health.MaximumHealth);
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
        ecsContext.ComponentManager.GetPackedPool<HealthComponent>().Add(entityId, new HealthComponent(50, 100));

        new Tank(new MathUtility(new Random(1))).Build(ecsContext.ComponentManager, entityId);

        var health = ecsContext.ComponentManager.GetPackedPool<HealthComponent>().GetReadonly(entityId);
        Assert.AreEqual((short)110, health.MaximumHealth);
        AssertHasHealthRegenBonusModifier(ecsContext.ComponentManager, entityId);
    }

    /// <summary>Regen has no stored field for Tank to have multiplied in place anymore (see HealthRegenSystem) -- its +10% bonus is a granted StatModifier instead, asserted here by presence/shape rather than by reading a HealthComponent field.</summary>
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

        var movement = ecsContext.ComponentManager.GetPackedPool<MovementComponent>().GetReadonly(entityId);
        // Goblin sets a fixed ActionCooldownFrames of 54; Engineer applies *0.9 and casts to
        // short (54 * 0.9m = 48.6 -> 48), then GoblinEngineerBlueprint applies its own *0.9 to
        // that already-truncated value and casts again (48 * 0.9m = 43.2 -> 43) -- each stage
        // rounds down independently, not one combined multiplication.
        Assert.AreEqual((short)43, movement.ActionCooldownFrames);
    }

    [TestMethod]
    public void RaceAndClassModules_Register_AsMultiComponentPools()
    {
        var ecsContext = BuildEcsContext();

        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<RaceComponent>());
        Assert.AreEqual(ComponentPoolType.Multi, ecsContext.ComponentManager.GetPoolType<RaceComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<ClassComponent>());
        Assert.AreEqual(ComponentPoolType.Multi, ecsContext.ComponentManager.GetPoolType<ClassComponent>());
    }
}