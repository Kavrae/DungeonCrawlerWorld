using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.Math;
using Game.Bootstrap;
using Game.Spawning;
using Game.Modules.Core.Components;
using Game.World;

namespace Tests.Spawning;

/// <summary>Guards EntityFactory.SkeletonComponentTypes' rule: the skeleton is written from definitions alone, and no build step ever writes a skeleton component.</summary>
[TestClass]
public sealed class BlueprintSkeletonTests
{
    private static List<string> SkeletonComponentsOf(ComponentManager componentManager, int entityId)
    {
        var entries = new List<InspectedComponentEntry>();
        new ComponentInspector(componentManager).CopyInspectionDataForEntity(entityId, entries);
        return [.. entries
            .Where(static entry => EntityFactory.SkeletonComponentTypes.Contains(entry.ComponentType))
            .Select(static entry => $"{entry.ComponentType.Name}: {entry.Value}")
            .Order(StringComparer.Ordinal)];
    }

    private static GameBootstrapResult Bootstrap() =>
        GameBootstrapper.Build(ValidatedMods.None, new Map(new Vector3Int(20, 20, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 1_000, initialComponentCapacity: 100);

    /// <summary>Builds blueprintId's skeleton, gives it a footprint no blueprint declares (so even a same-sized transform merge would show), then builds the rest and reports whether any skeleton component changed.</summary>
    private static bool WritesASkeletonComponent(GameBootstrapResult result, ushort blueprintId)
    {
        var ecs = result.EcsContext;
        var entityId = ecs.EntityManager.CreateEntity();
        result.Factory.BuildSkeleton(ecs.ComponentManager, entityId, blueprintId, seed: 1);
        ecs.ComponentManager.GetDirectPool<TransformComponent>().TrySet(entityId, new TransformComponent(TransformComponent.UnplacedOn(MapLayer.Ground), new Vector2Byte(7, 5)));
        var skeleton = SkeletonComponentsOf(ecs.ComponentManager, entityId);

        result.Factory.BuildComplete(ecs.ComponentManager, entityId, blueprintId, seed: 1, now: 0);

        var changed = !skeleton.SequenceEqual(SkeletonComponentsOf(ecs.ComponentManager, entityId));
        ecs.EntityManager.DestroyEntity(entityId);
        return changed;
    }

    /// <summary>Every registered definition, traits and pieces included: a build step that writes a skeleton component would overwrite (or, for a merged one, double) what the skeleton already holds.</summary>
    [TestMethod]
    public void NoBuildStep_WritesASkeletonComponent()
    {
        var result = Bootstrap();
        var offenders = new List<string>();

        for (var id = (ushort)1; id <= result.Definitions.Count; id++)
        {
            if (WritesASkeletonComponent(result, id))
            {
                offenders.Add(result.Definitions.Get(id).Name);
            }
        }

        Assert.IsEmpty(offenders, $"These write a skeleton component in a build step: {string.Join(", ", offenders)}.");
    }

    /// <summary>Guards the check itself: a build step that merges a transform must be caught, even one the same size as the blueprint's own.</summary>
    [TestMethod]
    public void ABuildStepThatWritesATransform_IsCaught()
    {
        var result = Bootstrap();
        var offenderId = result.Definitions.Register(new Game.Blueprints.BlueprintDefinition(Guid.NewGuid(), "Offender")
        {
            Build = static context => context.ComponentManager.Merge(context.EntityId, new TransformComponent(TransformComponent.UnplacedOn(MapLayer.Ground), new Vector2Byte(1, 1))),
        });

        Assert.IsTrue(WritesASkeletonComponent(result, offenderId));
    }

    [TestMethod]
    public void BuildSkeleton_GivesAnUnplacedTransform_OnTheBlueprintsLayerAndSize()
    {
        var result = Bootstrap();
        var ecs = result.EcsContext;
        var entityId = ecs.EntityManager.CreateEntity();

        result.Factory.BuildSkeleton(ecs.ComponentManager, entityId, result.Definitions.GetId(Game.Blueprints.Composites.GoblinForeman.Id), seed: 1);

        var transform = ecs.ComponentManager.GetDirectPool<TransformComponent>().GetReadonly(entityId);
        Assert.AreEqual(TransformComponent.UnplacedOn(MapLayer.Ground), transform.Position);
        Assert.AreEqual(new Vector2Byte(2, 2), transform.Size);
    }
}
