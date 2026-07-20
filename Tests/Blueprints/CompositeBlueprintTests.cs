using Engine.Bootstrap;
using Engine.ECS.Components;
using Engine.ECS.World;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Blueprints;
using Game.Blueprints.Classes;
using Game.Blueprints.Objects;
using Game.Blueprints.Races;
using Game.Modules;
using Game.Modules.Class;
using Game.Modules.Class.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Energy;
using Game.Modules.Energy.Components;
using Game.Modules.Health;
using Game.Modules.Movement;
using Game.Modules.Race;
using Game.Modules.Race.Components;
using Game.World;

namespace Tests.Blueprints;

[TestClass]
public sealed class CompositeBlueprintTests
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

    /// <summary>Minimal IBlueprint that stamps a recognizable name/description, for tests that only care about composition order and shape, not real game components.</summary>
    private sealed class MarkerBlueprint(string marker) : IBlueprint
    {
        public void Build(ComponentManager componentManager, int entityId) =>
            componentManager.Merge(entityId, new DisplayTextComponent(marker, marker));
    }

    [TestMethod]
    public void CompositeBlueprint_Build_AppliesAllPartsInOrder()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        var mathUtility = new MathUtility(new Random(1));

        var composite = new CompositeBlueprint([new Goblin(mathUtility), new Engineer()]);
        composite.Build(ecsContext.ComponentManager, entityId);

        Assert.IsTrue(ecsContext.ComponentManager.GetMultiPool<RaceComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetMultiPool<ClassComponent>().Has(entityId));

        // Goblin's fixed MaximumEnergy ceiling (100) with Engineer's own 5% bonus on top --
        // same composition CompositeBlueprint is meant to replace GoblinEngineerBlueprint's
        // hand-written "goblin.Build then engineer.Build" with.
        var energy = ecsContext.ComponentManager.GetPackedPool<EnergyComponent>().GetReadonly(entityId);
        Assert.AreEqual((short)105, energy.MaximumEnergy);
    }

    [TestMethod]
    public void CompositeBlueprint_Build_AppliesOverridesAfterAllParts()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        var composite = new CompositeBlueprint(
            [new MarkerBlueprint("PartA"), new MarkerBlueprint("PartB")],
            static (componentManager, id) => componentManager.Merge(id, new DisplayTextComponent("Override", "Override")));

        composite.Build(ecsContext.ComponentManager, entityId);

        var displayText = ecsContext.ComponentManager.GetDirectPool<DisplayTextComponent>().GetReadonly(entityId);
        StringAssert.Contains(displayText.Name, "PartA");
        StringAssert.Contains(displayText.Name, "PartB");
        StringAssert.Contains(displayText.Name, "Override");

        // DisplayTextComponent's merge action appends -- Override coming last in the string
        // confirms the overrides delegate really ran after both parts, not interleaved.
        Assert.IsGreaterThan(
            displayText.Name.IndexOf("PartB", StringComparison.Ordinal),
            displayText.Name.IndexOf("Override", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CompositeBlueprint_Build_SupportsPartsWithNoRaceOrClass()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        // A "wall with magical properties" style composite: no race/class blueprint at all.
        var magicalWall = new CompositeBlueprint([new Wall(), new MarkerBlueprint("Magical")]);
        magicalWall.Build(ecsContext.ComponentManager, entityId);

        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<GlyphComponent>().Has(entityId));
        Assert.IsFalse(ecsContext.ComponentManager.GetMultiPool<RaceComponent>().Has(entityId));
    }

    [TestMethod]
    public void BlueprintVariantSet_Build_AppliesBaseAndExactlyOneVariant()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        var variantSet = new BlueprintVariantSet(
            new MarkerBlueprint("Base"),
            [new MarkerBlueprint("VariantA"), new MarkerBlueprint("VariantB")],
            new MathUtility(new Random(1)));

        variantSet.Build(ecsContext.ComponentManager, entityId);

        var displayText = ecsContext.ComponentManager.GetDirectPool<DisplayTextComponent>().GetReadonly(entityId);
        StringAssert.Contains(displayText.Name, "Base");

        var containsA = displayText.Name.Contains("VariantA", StringComparison.Ordinal);
        var containsB = displayText.Name.Contains("VariantB", StringComparison.Ordinal);
        Assert.AreNotEqual(containsA, containsB, "Exactly one variant should be applied, never both or neither.");
    }

    [TestMethod]
    public void BlueprintVariantSet_Build_ThrowsWhenNoVariantsProvided()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        var variantSet = new BlueprintVariantSet(new MarkerBlueprint("Base"), [], new MathUtility());

        Assert.ThrowsExactly<InvalidOperationException>(() => variantSet.Build(ecsContext.ComponentManager, entityId));
    }

    [TestMethod]
    public void CompositeBlueprint_Build_SupportsNestedVariantSetAsAPart()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        var nestedVariantSet = new BlueprintVariantSet(
            new MarkerBlueprint("NestedBase"),
            [new MarkerBlueprint("NestedVariant")],
            new MathUtility(new Random(1)));

        var composite = new CompositeBlueprint([new MarkerBlueprint("Outer"), nestedVariantSet]);
        composite.Build(ecsContext.ComponentManager, entityId);

        var displayText = ecsContext.ComponentManager.GetDirectPool<DisplayTextComponent>().GetReadonly(entityId);
        StringAssert.Contains(displayText.Name, "Outer");
        StringAssert.Contains(displayText.Name, "NestedBase");
        StringAssert.Contains(displayText.Name, "NestedVariant");
    }
}
