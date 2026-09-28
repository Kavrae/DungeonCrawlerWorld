using Engine.Bootstrap;
using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Blueprints;
using Game.Blueprints.Classes;
using Game.Blueprints.Composites;
using Game.Blueprints.Parts;
using Game.Blueprints.Races;
using Game.Spawning;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Class;
using Game.Modules.Class.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Currency;
using Game.Modules.Health;
using Game.Modules.Inventory;
using Game.Modules.Movement;
using Game.Modules.ProcessingTier;
using Game.Modules.Race;
using Game.Modules.Race.Components;
using Game.Modules.StatModifiers;
using Game.World;

namespace Tests.Blueprints;

[TestClass]
public sealed class BlueprintCompositionTests
{
    private static EcsContext BuildEcsContext()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var context = new GameModuleContext(world, new MathUtility(), new EventBus()) { PlayerQuery = world, EntityMoveSync = new WorldEventSync(world) };

        return BuiltInTestModules.Build(context, 100, 50);
    }

    /// <summary>A build step that appends marker to the entity's name, so the name spells out the order the parts built in.</summary>
    private static Action<BlueprintContext> MarkerBuild(string marker) =>
        context => context.ComponentManager.Merge(context.EntityId, new DisplayTextComponent(marker, marker));

    private static BlueprintDefinition Marker(string name, params Guid[] includes) =>
        new(Guid.NewGuid(), name) { Includes = includes, Build = MarkerBuild(name) };

    private static (EcsContext Ecs, int EntityId) Build(BlueprintRegistry definitions, ushort blueprintId)
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        new EntityFactory(definitions, ecsContext.EntityManager.Keys).Build(ecsContext.ComponentManager, entityId, blueprintId, seed: 1, now: 0);
        return (ecsContext, entityId);
    }

    private static string NameOf((EcsContext Ecs, int EntityId) built) =>
        built.Ecs.ComponentManager.GetPackedPool<DisplayTextComponent>().GetReadonly(built.EntityId).Name;

    [TestMethod]
    public void Build_BuildsIncludesInListOrder_ThenTheDefinitionsOwnStep()
    {
        var definitions = new BlueprintRegistry();
        var first = Marker("First");
        var second = Marker("Second");
        definitions.Register(first);
        definitions.Register(second);
        var composite = definitions.Register(Marker("Own", first.Id, second.Id));

        Assert.AreEqual("First Second Own", NameOf(Build(definitions, composite)));
    }

    [TestMethod]
    public void Build_CompositeOfComposites_BuildsEveryLevel()
    {
        var definitions = new BlueprintRegistry();
        var inner = Marker("Inner");
        var middle = Marker("Middle", inner.Id);
        var trait = Marker("Trait");
        definitions.Register(inner);
        definitions.Register(middle);
        definitions.Register(trait);
        var outer = definitions.Register(Marker("Outer", middle.Id, trait.Id));

        Assert.AreEqual("Inner Middle Trait Outer", NameOf(Build(definitions, outer)));
    }

    /// <summary>Two includes sharing a part build it once, where it was first reached -- a goblin foreman whose Boss also included Goblin would still be one goblin.</summary>
    [TestMethod]
    public void Build_SharedInclude_IsBuiltOnce()
    {
        var definitions = new BlueprintRegistry();
        var shared = Marker("Shared");
        var left = Marker("Left", shared.Id);
        var right = Marker("Right", shared.Id);
        definitions.Register(shared);
        definitions.Register(left);
        definitions.Register(right);
        var both = definitions.Register(Marker("Both", left.Id, right.Id));

        Assert.AreEqual("Shared Left Right Both", NameOf(Build(definitions, both)));
    }

    [TestMethod]
    public void Build_RaceAndClassFacets_GrantTheirSlots()
    {
        var built = Build(BlueprintTestContext.Definitions, BlueprintTestContext.GoblinEngineerBlueprint);
        var componentManager = built.Ecs.ComponentManager;

        Assert.AreEqual(BlueprintTestContext.Definitions.Races.GetId(Goblin.Id), componentManager.GetPackedPool<RaceSlotsComponent>().GetReadonly(built.EntityId).Primary);
        Assert.AreEqual(BlueprintTestContext.Definitions.Classes.GetId(Engineer.Id), componentManager.GetPackedPool<ClassSlotsComponent>().GetReadonly(built.EntityId).Primary);
    }

    [TestMethod]
    public void Build_DefinitionWithNoRaceOrClass_GrantsNeither()
    {
        var definitions = new BlueprintRegistry();
        var prop = definitions.Register(Marker("Prop"));

        var built = Build(definitions, prop);

        Assert.IsFalse(built.Ecs.ComponentManager.GetPackedPool<RaceSlotsComponent>().Has(built.EntityId));
        Assert.IsFalse(built.Ecs.ComponentManager.GetPackedPool<ClassSlotsComponent>().Has(built.EntityId));
        Assert.IsFalse(definitions.Resolve(prop).Deferrable);
    }

    /// <summary>A mod replacing a built-in by Guid changes every composite that includes it, not just the built-in spawned alone.</summary>
    [TestMethod]
    public void Register_ReplacingAnInclude_ChangesTheCompositeIncludingIt()
    {
        var definitions = new BlueprintRegistry();
        var part = Marker("Original");
        definitions.Register(part);
        var composite = definitions.Register(Marker("Composite", part.Id));
        Assert.AreEqual("Original Composite", NameOf(Build(definitions, composite)));

        definitions.Register(new BlueprintDefinition(part.Id, "Replacement") { Build = MarkerBuild("Replacement") });

        Assert.AreEqual("Replacement Composite", NameOf(Build(definitions, composite)));
    }

    [TestMethod]
    public void GoblinForeman_IsGoblinEngineerThenBoss()
    {
        var definitions = BlueprintTestContext.Definitions;
        var foreman = definitions.Resolve(definitions.GetId(GoblinForeman.Id));

        CollectionAssert.AreEqual(
            new[] { Goblin.Id, Engineer.Id, GoblinEngineer.Id, Boss.Id, GoblinForeman.Id },
            foreman.BuildOrder.Select(id => definitions.Get(id).Id).ToArray());
        CollectionAssert.AreEqual(new[] { definitions.GetId(Goblin.Id) }, foreman.Races.ToArray());
        StringAssert.EndsWith(foreman.NameFor(seed: 1), $" {Engineer.Name} {Boss.Name}");
        Assert.IsTrue(foreman.Deferrable);
    }

    [TestMethod]
    public void GoblinForeman_SpawnsWithTwiceAGoblinEngineersHealth()
    {
        var engineer = Build(BlueprintTestContext.Definitions, BlueprintTestContext.GoblinEngineerBlueprint);
        var foreman = Build(BlueprintTestContext.Definitions, BlueprintTestContext.Definitions.GetId(GoblinForeman.Id));

        EntityBodyParts.For(engineer.Ecs.ComponentManager, BlueprintTestContext.Definitions).TryGetTotals(engineer.EntityId, out var engineerHealth, out _);
        EntityBodyParts.For(foreman.Ecs.ComponentManager, BlueprintTestContext.Definitions).TryGetTotals(foreman.EntityId, out var foremanHealth, out _);

        Assert.AreEqual(engineerHealth * 2, foremanHealth, 0.01f);
    }
}
