using Engine.Bootstrap;
using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Blueprints.Classes;
using Game.Blueprints.NPCs.Generic;
using Game.Blueprints.Objects;
using Game.Blueprints.Races;
using Game.Blueprints.Terrain;
using Game.Modules;
using Game.Modules.Class;
using Game.Modules.Class.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Energy;
using Game.Modules.Energy.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.Modules.Race;
using Game.Modules.Race.Components;
using Game.World;

namespace Tests.Blueprints;

[TestClass]
public sealed class BlueprintTests
{
    private static EcsContext BuildEcsContext()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var mathUtility = new MathUtility();

        var movementModule = new MovementModule();
        movementModule.Configure(new GameModuleContext(world, mathUtility, new EventBus()));

        IReadOnlyList<IModule> modules =
        [
            new CoreModule(),
            new EnergyModule(),
            new HealthModule(),
            movementModule,
            new RaceModule(),
            new ClassModule(),
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
    public void Goblin_Build_SetsRaceEnergyHealthMovementAndTransform()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new Goblin(new MathUtility(new Random(1))).Build(ecsContext.ComponentManager, entityId);

        var racePool = ecsContext.ComponentManager.GetMultiPool<RaceComponent>();
        Assert.IsTrue(racePool.Has(entityId));
        Assert.AreEqual("Goblin", racePool.GetReadonlyByDenseIndex(racePool.GetFirstDenseIndex(entityId)).Name);
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<EnergyComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<HealthComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));
    }

    [TestMethod]
    public void Fairy_Build_SetsRaceEnergyHealthMovementAndTransform()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new Fairy(new MathUtility(new Random(1))).Build(ecsContext.ComponentManager, entityId);

        Assert.IsTrue(ecsContext.ComponentManager.GetMultiPool<RaceComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<EnergyComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<HealthComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));
    }

    [TestMethod]
    public void Engineer_Build_AppliesEnergyBonusWhenEnergyComponentPresent()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        ecsContext.ComponentManager.GetPackedPool<EnergyComponent>().Add(entityId, new EnergyComponent(50, 10, 100));

        new Engineer().Build(ecsContext.ComponentManager, entityId);

        var energy = ecsContext.ComponentManager.GetPackedPool<EnergyComponent>().GetReadonly(entityId);
        Assert.AreEqual((short)105, energy.MaximumEnergy);
        Assert.AreEqual((short)10, energy.EnergyRecharge); // 10 * 1.05m rounds down to 10.
        Assert.IsTrue(ecsContext.ComponentManager.GetMultiPool<ClassComponent>().Has(entityId));
    }

    [TestMethod]
    public void Engineer_Build_AddsBaselineEnergyWhenEnergyComponentAbsent()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new Engineer().Build(ecsContext.ComponentManager, entityId);

        // No race ran first, so Engineer merges its own baseline instead of silently doing
        // nothing -- the class still functions when composed (or used) without a race.
        var energy = ecsContext.ComponentManager.GetPackedPool<EnergyComponent>().GetReadonly(entityId);
        Assert.AreEqual((short)100, energy.MaximumEnergy);
        Assert.AreEqual((short)5, energy.EnergyRecharge);
        Assert.IsTrue(ecsContext.ComponentManager.GetMultiPool<ClassComponent>().Has(entityId));
    }

    [TestMethod]
    public void Tank_Build_AddsBaselineHealthWhenHealthComponentAbsent()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new Tank().Build(ecsContext.ComponentManager, entityId);

        // No race ran first, so Tank merges its own baseline instead of silently doing
        // nothing -- the class still functions when composed (or used) without a race.
        var health = ecsContext.ComponentManager.GetPackedPool<HealthComponent>().GetReadonly(entityId);
        Assert.AreEqual((short)100, health.MaximumHealth);
        Assert.AreEqual((short)10, health.HealthRegen);
        Assert.IsTrue(ecsContext.ComponentManager.GetMultiPool<ClassComponent>().Has(entityId));
    }

    /// <summary>
    /// Engineer and Goblin are each independently order-independent: composing them in
    /// reverse (class before race) never throws or drops the class's mechanic -- Engineer
    /// merges its own baseline energy since none exists yet, then Goblin's own energy merges
    /// on top via EnergyModule's registered merge action. The exact resulting numbers depend
    /// on order, but the entity always ends up with a working EnergyComponent either way.
    /// </summary>
    [TestMethod]
    public void EngineerThenGoblin_ComposedInReverseOrder_StillProducesAWorkingEntity()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        var mathUtility = new MathUtility(new Random(1));

        new Engineer().Build(ecsContext.ComponentManager, entityId);
        new Goblin(mathUtility).Build(ecsContext.ComponentManager, entityId);

        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<EnergyComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetMultiPool<ClassComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetMultiPool<RaceComponent>().Has(entityId));
    }

    [TestMethod]
    public void Tank_Build_AppliesHealthBonusWhenHealthComponentPresent()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        ecsContext.ComponentManager.GetPackedPool<HealthComponent>().Add(entityId, new HealthComponent(50, 10, 100));

        new Tank().Build(ecsContext.ComponentManager, entityId);

        var health = ecsContext.ComponentManager.GetPackedPool<HealthComponent>().GetReadonly(entityId);
        Assert.AreEqual((short)110, health.MaximumHealth);
        Assert.AreEqual((short)11, health.HealthRegen);
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
    public void GoblinEngineerBlueprint_Build_AppliesCompoundEnergyBonusOnTopOfEngineersOwn()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        var mathUtility = new MathUtility(new Random(1));

        new GoblinEngineerBlueprint(new Goblin(mathUtility), new Engineer()).Build(ecsContext.ComponentManager, entityId);

        var energy = ecsContext.ComponentManager.GetPackedPool<EnergyComponent>().GetReadonly(entityId);
        // Goblin sets a random MaximumEnergy of 100 (its ceiling); Engineer applies *1.05,
        // then GoblinEngineerBlueprint applies another *1.1 on top -- both bonuses stack.
        Assert.AreEqual((short)(100 * 1.05m * 1.1m), energy.MaximumEnergy);
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