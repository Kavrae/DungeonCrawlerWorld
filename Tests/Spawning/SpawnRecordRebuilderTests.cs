using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Classes;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Spawning;
using Game.Floors;
using Game.Modules.Core.Components;
using Game.Modules.Crawler.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Game.Blueprints;

namespace Tests.Spawning;

[TestClass]
public sealed class SpawnRecordRebuilderTests
{
    /// <summary>Compared on their own terms below: a stack's instance id and acquisition time are fresh per build, and a modifier's source names the entity it was built on.</summary>
    private static readonly Type[] ComparedSeparately = [typeof(InventoryItemStackComponent), typeof(StatModifierComponent)];

    /// <summary>Not part of what a spawn record rebuilds: placement, tiering and crawler numbers are the spawner's, and the action-lock stagger counts from the frame the creature was built on.</summary>
    private static readonly Type[] WrittenAfterBuild = [typeof(TransformComponent), typeof(ActionLockComponent), typeof(ProcessingTierComponent), typeof(CrawlerComponent)];

    private sealed record Creature(List<string> Components, List<string> Stacks, List<string> Modifiers);

    private static GameSession Bootstrap(Map map, UniqueNumberAllocator? crawlerNumbers = null) =>
        GameBootstrapper.Build(ValidatedMods.None, map, new MathUtility(new Random(1)), initialEntityCapacity: 1_000, initialComponentCapacity: 100, crawlerNumbers: crawlerNumbers);

    private static Creature Read(ComponentManager componentManager, int entityId, params Type[] excluded)
    {
        var entries = new List<InspectedComponentEntry>();
        new ComponentInspector(componentManager).CopyInspectionDataForEntity(entityId, entries);

        var components = entries
            .Where(entry => !ComparedSeparately.Contains(entry.ComponentType) && !excluded.Contains(entry.ComponentType))
            .Select(static entry => $"{entry.ComponentType.Name}: {entry.Value}")
            .Order(StringComparer.Ordinal)
            .ToList();

        var stacks = new List<InventoryItemStackComponent>();
        componentManager.GetMultiPool<InventoryItemStackComponent>().CopyAll(entityId, stacks);

        var modifiers = new List<StatModifierComponent>();
        componentManager.GetMultiPool<StatModifierComponent>().CopyAll(entityId, modifiers);

        return new Creature(
            components,
            stacks.Select(static stack => $"{stack.ItemDefinitionId} x{stack.Quantity} {stack.IsDivergent} {stack.Override?.GetHashCode()}").Order(StringComparer.Ordinal).ToList(),
            modifiers.Select(static modifier => $"{modifier.Target} {modifier.Operation} {modifier.Polarity} {modifier.Magnitude} {modifier.ExpiresAtFrame} {modifier.Source.Kind}").Order(StringComparer.Ordinal).ToList());
    }

    private static Creature RebuildDefaults(GameSession result, SpawnRecordComponent record, params Type[] excluded)
    {
        Creature? rebuilt = null;
        result.Internals.SpawnRecordRebuilder.Rebuild(record, (componentManager, entityId) => rebuilt = Read(componentManager, entityId, excluded));
        return rebuilt!;
    }

    private static void AssertSame(Creature expected, Creature actual)
    {
        CollectionAssert.AreEqual(expected.Components, actual.Components);
        CollectionAssert.AreEqual(expected.Stacks, actual.Stacks);
        CollectionAssert.AreEqual(expected.Modifiers, actual.Modifiers);
    }

    private static ushort Blueprint(GameSession result, params Guid[] parts) =>
        parts.Length == 1
            ? result.Catalogs.Definitions.GetId(parts[0])
            : result.Catalogs.Definitions.Register(new BlueprintDefinition(Guid.NewGuid(), "Test composite") { Includes = parts });

    [TestMethod]
    public void Rebuild_ReproducesEveryBlueprintBuiltComponent_ForEachRace()
    {
        var result = Bootstrap(new Map(new Vector3Int(20, 20, 3)));
        var ecs = result.EcsContext;
        var builder = new EntityBuilder(result.Catalogs.Definitions, result.Catalogs.Auras, result.Catalogs.ActionCatalog, ecs.EntityManager.Keys);

        foreach (var race in new[] { Goblin.Id, Fairy.Id, Ghost.Id, Human.Id })
        {
            for (var seed = 0u; seed < 5; seed++)
            {
                var entityId = ecs.EntityManager.CreateEntity();
                var blueprintId = Blueprint(result, race);
                builder.Build(ecs.ComponentManager, entityId, blueprintId, seed, now: 0);

                AssertSame(Read(ecs.ComponentManager, entityId), RebuildDefaults(result, new SpawnRecordComponent(blueprintId, seed)));
            }
        }
    }

    /// <summary>Guards the comparison itself: if Read ever stopped seeing the rolled components, every rebuild would compare equal.</summary>
    [TestMethod]
    public void Rebuild_WithAnotherSeed_DiffersFromTheCreature()
    {
        var result = Bootstrap(new Map(new Vector3Int(20, 20, 3)));
        var ecs = result.EcsContext;
        var entityId = ecs.EntityManager.CreateEntity();
        var blueprintId = Blueprint(result, Goblin.Id);
        new EntityBuilder(result.Catalogs.Definitions, result.Catalogs.Auras, result.Catalogs.ActionCatalog, ecs.EntityManager.Keys).Build(ecs.ComponentManager, entityId, blueprintId, seed: 1, now: 0);
        var creature = Read(ecs.ComponentManager, entityId);

        var others = Enumerable.Range(2, 10).Select(seed => RebuildDefaults(result, new SpawnRecordComponent(blueprintId, (uint)seed)));

        Assert.IsGreaterThan(EntityFactory.SkeletonComponentTypes.Count, creature.Components.Count, "A built creature holds more than a skeleton does.");
        Assert.IsTrue(others.Any(other => !other.Components.SequenceEqual(creature.Components) || !other.Stacks.SequenceEqual(creature.Stacks)));
    }

    [TestMethod]
    public void Rebuild_ReproducesARaceWithTwoClasses()
    {
        var result = Bootstrap(new Map(new Vector3Int(20, 20, 3)));
        var ecs = result.EcsContext;
        var entityId = ecs.EntityManager.CreateEntity();
        var blueprintId = Blueprint(result, Goblin.Id, Engineer.Id, Tank.Id);

        new EntityBuilder(result.Catalogs.Definitions, result.Catalogs.Auras, result.Catalogs.ActionCatalog, ecs.EntityManager.Keys).Build(ecs.ComponentManager, entityId, blueprintId, seed: 99, now: 0);

        AssertSame(Read(ecs.ComponentManager, entityId), RebuildDefaults(result, new SpawnRecordComponent(blueprintId, 99)));
    }

    [TestMethod]
    public void Build_RunsRacesBeforeClasses()
    {
        var result = Bootstrap(new Map(new Vector3Int(20, 20, 3)));
        var ecs = result.EcsContext;
        var entityId = ecs.EntityManager.CreateEntity();

        new EntityBuilder(result.Catalogs.Definitions, result.Catalogs.Auras, result.Catalogs.ActionCatalog, ecs.EntityManager.Keys).Build(ecs.ComponentManager, entityId, Blueprint(result, Goblin.Id, Engineer.Id), seed: 1, now: 0);

        // Goblin's 54-frame lock, then Engineer's 10% reduction on top of it.
        Assert.AreEqual((ushort)48, ecs.ComponentManager.GetPackedPool<ActionLockComponent>().GetReadonly(entityId).StandardLockFrames);
    }

    [TestMethod]
    public void Build_WritesTheSpawnRecord()
    {
        var result = Bootstrap(new Map(new Vector3Int(20, 20, 3)));
        var ecs = result.EcsContext;
        var entityId = ecs.EntityManager.CreateEntity();
        var blueprintId = Blueprint(result, Fairy.Id);

        new EntityBuilder(result.Catalogs.Definitions, result.Catalogs.Auras, result.Catalogs.ActionCatalog, ecs.EntityManager.Keys).Build(ecs.ComponentManager, entityId, blueprintId, seed: 1234, now: 0);

        Assert.AreEqual(new SpawnRecordComponent(blueprintId, 1234), ecs.ComponentManager.GetDirectPool<SpawnRecordComponent>().GetReadonly(entityId));
    }

    [TestMethod]
    public void Build_DifferentSeeds_RollDifferentCreatures()
    {
        var result = Bootstrap(new Map(new Vector3Int(20, 20, 3)));
        var ecs = result.EcsContext;
        var builder = new EntityBuilder(result.Catalogs.Definitions, result.Catalogs.Auras, result.Catalogs.ActionCatalog, ecs.EntityManager.Keys);
        var blueprintId = Blueprint(result, Goblin.Id);

        var loadouts = Enumerable.Range(0, 20).Select(seed =>
        {
            var entityId = ecs.EntityManager.CreateEntity();
            builder.Build(ecs.ComponentManager, entityId, blueprintId, (uint)seed, now: 0);
            return string.Join(";", Read(ecs.ComponentManager, entityId).Stacks);
        }).ToHashSet();

        Assert.IsGreaterThan(1, loadouts.Count);
    }

    [TestMethod]
    public void Rebuild_LeavesNothingBehindInTheStagingWorld()
    {
        var result = Bootstrap(new Map(new Vector3Int(20, 20, 3)));
        var record = new SpawnRecordComponent(Blueprint(result, Goblin.Id, Tank.Id), 5);
        var stagingIds = new List<int>();

        result.Internals.SpawnRecordRebuilder.Rebuild(record, (_, entityId) => stagingIds.Add(entityId));
        result.Internals.SpawnRecordRebuilder.Rebuild(record, (componentManager, entityId) =>
        {
            stagingIds.Add(entityId);
            Assert.IsTrue(componentManager.GetDirectPool<SpawnRecordComponent>().Has(entityId));
        });

        Assert.AreEqual(stagingIds[0], stagingIds[1]);
        Assert.IsFalse(result.EcsContext.ComponentManager.GetDirectPool<SpawnRecordComponent>().Has(stagingIds[0]));
    }

    /// <summary>A populated neighborhood's creatures depend on their spawn records alone -- not on how many creatures population rolled before them.</summary>
    [TestMethod]
    public void PopulatedCreatures_RebuildFromTheirSpawnRecords()
    {
        var mathUtility = new MathUtility(new Random(3));
        var result = Bootstrap(new Map(new Vector3Int(60, 60, 3)), new UniqueNumberAllocator(1, 1, 24));
        var world = result.World;
        var ecs = result.EcsContext;
        FloorBuilder.PopulateFloor(world, ecs, new NeighborhoodRecords(mathUtility), result.Internals.Factory, result.Catalogs.Terrain, result.Catalogs.Auras, result.Catalogs.Definitions);

        var spawnRecords = ecs.ComponentManager.GetDirectPool<SpawnRecordComponent>();
        var checkedCount = 0;
        for (var entityId = 0; entityId < spawnRecords.Capacity && checkedCount < 40; entityId++)
        {
            if (spawnRecords.TryGetReadonly(entityId, out var record))
            {
                AssertSame(Read(ecs.ComponentManager, entityId, WrittenAfterBuild), RebuildDefaults(result, record, WrittenAfterBuild));
                checkedCount++;
            }
        }

        Assert.IsGreaterThan(10, checkedCount);
    }
}
