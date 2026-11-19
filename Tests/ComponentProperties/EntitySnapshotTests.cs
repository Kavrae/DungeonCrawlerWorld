using Engine.ECS.Components;
using Engine.ECS.Properties;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Tiles;
using Game.Blueprints;
using Game.Bootstrap;
using Game.ComponentProperties;
using Game.Floors;
using Game.Modules.Auras.Components;
using Game.Modules.Burning.Components;
using Game.World;

namespace Tests.ComponentProperties;

/// <summary>Entity snapshots: a built world's entities captured through their tables and restored into a second build of the same content, reading back equal; links, frames and the check.</summary>
[TestClass]
public sealed class EntitySnapshotTests
{
    private const int FramesPerSecond = 60;
    private static readonly MapBounds WorldBounds = new(0, 0, 1024, 1024, 3);

    [TestMethod]
    public void TheGame_GivesEverySavedPropertyAPersistentForm()
    {
        var pass = BuiltInTestModules.Build(new Map(new Vector3Int(20, 20, 3)));

        Assert.IsEmpty(new GameEntitySnapshots(pass.Context, pass.World).Check());
    }

    [TestMethod]
    public void AMeaningWithNoPersistentForm_FailsTheCheckByName()
    {
        var components = new ComponentManager(initialEntityCapacity: 4, initialComponentCapacity: 4);
        components.RegisterPackedPool<SnapshotFixtureComponent>(static (ref existing, incoming) => existing = incoming);

        var problems = EntitySnapshots.Check(components, ComponentPropertyPersistence.WithEngineMeanings());

        Assert.HasCount(1, problems);
        StringAssert.Contains(problems[0], "SnapshotFixtureComponent.Unpersisted: meaning NoSuchMeaning has no persistent form");
    }

    /// <summary>The appendix's "saved: no" list, and nothing else: derived values (TargetSelection's in each component holding one), ProcessingTierComponent's tier and a relationship's sources.</summary>
    [TestMethod]
    public void TheMembersASnapshotLeavesOut_AreExactlyTheAppendixsList()
    {
        var pass = BuiltInTestModules.Build(new Map(new Vector3Int(20, 20, 3)));
        var left = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var pool in pass.EcsContext.ComponentManager.PoolsInRegistrationOrder.Cast<IDescribedComponentPool>())
        {
            CollectUnsaved(pool.PropertyTable!.Properties, ComponentName(pool.ComponentType), left);
        }

        Assert.AreEqual(
            string.Join(", ", new[]
            {
                "AbilityScoresComponent.Total",
                "ClassMembershipComponent.IsPermanent",
                "ClassSlotsComponent.Primary",
                "InventoryItemStackComponent.ItemDefinitionId",
                "ParalysisTimerComponent.StackCount",
                "PendingActionActivationComponent.Selection.HasMarkedEntity",
                "PendingItemActivationComponent.Selection.HasMarkedEntity",
                "PendingWindupComponent.Selection.HasMarkedEntity",
                "ProcessingTierComponent.Tier",
                "RaceSlotsComponent.Primary",
                "RelatedSourceComponent.SourceEntityId",
            }),
            string.Join(", ", left));
    }

    [TestMethod]
    public void EveryEntityOfABuiltWorld_RestoresIntoASecondBuild_ReadingBackEqual()
    {
        var source = BuildPopulatedWorld();
        var entityIds = BuiltEntities(source);
        var captured = new GameEntitySnapshots(source.Context, source.World).Capture(entityIds);
        Assert.IsEmpty(captured.Problems, string.Join(" | ", captured.Problems.Take(25)));

        var target = BuildEmptyWorldAt(source.Context.SimulationClock.CurrentFrame, source.Context.ProcessingTierResolver.ReferencePosition!.Value);
        var targetSnapshots = new GameEntitySnapshots(target.Context, target.World);
        var restored = targetSnapshots.Restore(captured.Json!);

        Assert.IsEmpty(restored.Problems, string.Join(" | ", restored.Problems.Take(25)));
        Assert.HasCount(entityIds.Count, restored.EntityIds);
        Assert.IsGreaterThan(1_000, entityIds.Count, "Precondition: a real population.");
        AssertSameJson(captured.Json!, targetSnapshots.Capture(restored.EntityIds).Json!);
    }

    /// <summary>Every blueprint BlueprintsModule registers that can be spawned alone, built, and again with a class applied, after a second of play.</summary>
    [TestMethod]
    public void EveryBlueprint_BuiltAndWithAClassApplied_RestoresIntoASecondBuild_ReadingBackEqual()
    {
        var source = BuiltInTestModules.Build(new Map(new Vector3Int(256, 256, 3)), sessionSeed: 1, initialEntityCapacity: 2000, initialComponentCapacity: 500);
        var definitions = source.Context.Definitions;
        var classId = definitions.GetId(Game.Blueprints.Classes.Tank.Id);
        var spawned = new List<int>();
        var slot = 0;
        for (var id = (ushort)1; id <= definitions.Count; id++)
        {
            if (!definitions.Resolve(id).IsSpawnable)
            {
                continue;
            }

            foreach (var withClass in (bool[])[false, true])
            {
                var entityId = source.Factory.Spawn(new Game.Spawning.SpawnRequest(id, 4 + slot % 40 * 6, 4 + slot / 40 * 6));
                slot++;
                if (entityId == Game.Spawning.EntityFactory.NoEntity)
                {
                    continue;
                }

                if (withClass)
                {
                    source.Context.EntityFactory.Skeletons.EnsureBuilt(entityId);
                    source.Factory.Apply(entityId, classId);
                }

                spawned.Add(entityId);
            }
        }

        Simulate(source, FramesPerSecond);
        var builtIds = spawned.Where(entityId => source.EcsContext.EntityManager.EntityExists(entityId) && !source.Context.EntityFactory.Skeletons.IsSkeleton(entityId)).ToList();
        var captured = new GameEntitySnapshots(source.Context, source.World).Capture(builtIds);
        Assert.IsEmpty(captured.Problems, string.Join(" | ", captured.Problems.Take(25)));

        var target = BuiltInTestModules.Build(new Map(new Vector3Int(256, 256, 3)), sessionSeed: 1, initialEntityCapacity: 2000, initialComponentCapacity: 500);
        Simulate(target, FramesPerSecond);
        var targetSnapshots = new GameEntitySnapshots(target.Context, target.World);
        var restored = targetSnapshots.Restore(captured.Json!);

        Assert.IsEmpty(restored.Problems, string.Join(" | ", restored.Problems.Take(25)));
        Assert.IsGreaterThan(20, builtIds.Count, "Precondition: most blueprints spawn built.");
        AssertSameJson(captured.Json!, targetSnapshots.Capture(restored.EntityIds).Json!);
    }

    [TestMethod]
    public void AnAnchorAndItsOwner_RestoreLinked_AndAnAnchorAloneRestoresUnlinked_WithTheLinkListed()
    {
        var source = BuiltInTestModules.Build(new Map(new Vector3Int(20, 20, 3)));
        var owner = source.Factory.Spawn(Game.Blueprints.NPCs.Generic.TestDummyBlueprint.Id, 2, 2);
        source.Context.EntityFactory.Skeletons.EnsureBuilt(owner);
        var anchor = source.Factory.Spawn(Game.Blueprints.Objects.AuraAnchor.Id, 5, 5);
        var components = source.EcsContext.ComponentManager;
        components.GetRelationship<AuraAnchorOwnerLink>().Link(anchor, new AuraAnchorOwnerLink(components.Keys.GetKey(owner), HeldGrantKey: 0));
        var snapshots = new GameEntitySnapshots(source.Context, source.World);

        var both = BuiltInTestModules.Build(new Map(new Vector3Int(20, 20, 3)));
        var restoredBoth = new GameEntitySnapshots(both.Context, both.World).Restore(snapshots.Capture([owner, anchor]).Json!);
        var alone = BuiltInTestModules.Build(new Map(new Vector3Int(20, 20, 3)));
        var restoredAlone = new GameEntitySnapshots(alone.Context, alone.World).Restore(snapshots.Capture([anchor]).Json!);

        Assert.IsEmpty(restoredBoth.Problems);
        Assert.IsTrue(both.EcsContext.ComponentManager.GetRelationship<AuraAnchorOwnerLink>().TryGetTargetEntityId(restoredBoth.EntityIds[1], out var restoredOwner));
        Assert.AreEqual(restoredBoth.EntityIds[0], restoredOwner);
        Assert.HasCount(1, restoredAlone.Problems);
        StringAssert.Contains(restoredAlone.Problems[0], nameof(AuraAnchorOwnerLink));
        Assert.IsFalse(alone.EcsContext.ComponentManager.GetRelationship<AuraAnchorOwnerLink>().TryGetTargetEntityId(restoredAlone.EntityIds[0], out _));
    }

    [TestMethod]
    public void AFrame_RestoresAsFarFromTheRestoreFrameAsItWasFromTheCapture_AndNeverStaysNever()
    {
        var source = BuiltInTestModules.Build(new Map(new Vector3Int(20, 20, 3)));
        Simulate(source, 3 * FramesPerSecond);
        var entityId = source.Factory.Spawn(Game.Blueprints.NPCs.Generic.TestDummyBlueprint.Id, 2, 2);
        source.Context.EntityFactory.Skeletons.EnsureBuilt(entityId);
        var now = source.Context.SimulationClock.CurrentFrame;
        var burns = source.EcsContext.ComponentManager.GetMultiPool<BodyPartBurningTimerComponent>();
        burns.Add(entityId, new BodyPartBurningTimerComponent(partId: 0, stackCount: 1, nextTickFrame: (uint)(now + 120), ActionSource.Admin));
        burns.Add(entityId, new BodyPartBurningTimerComponent(partId: 1, stackCount: 1, nextTickFrame: FrameDeadline.Never, ActionSource.Admin));
        var json = new GameEntitySnapshots(source.Context, source.World).Capture([entityId]).Json!;

        var target = BuiltInTestModules.Build(new Map(new Vector3Int(20, 20, 3)));
        Simulate(target, FramesPerSecond);
        var restored = new GameEntitySnapshots(target.Context, target.World).Restore(json);

        var targetNow = target.Context.SimulationClock.CurrentFrame;
        var restoredBurns = target.EcsContext.ComponentManager.GetMultiPool<BodyPartBurningTimerComponent>();
        var deadlines = new List<uint>();
        for (var denseIndex = restoredBurns.GetFirstDenseIndex(restored.EntityIds[0]); denseIndex != -1; denseIndex = restoredBurns.GetNextDenseIndex(denseIndex))
        {
            deadlines.Add(restoredBurns.GetReadonlyByDenseIndex(denseIndex).NextTickFrame);
        }

        CollectionAssert.AreEquivalent(new[] { (uint)(targetNow + 120), FrameDeadline.Never }, deadlines);
    }

    private static void CollectUnsaved(IReadOnlyList<ComponentPropertyDescriptor> properties, string owner, SortedSet<string> left)
    {
        foreach (var property in properties)
        {
            var name = $"{owner}.{property.Name}";
            if ((property.Flags & (ComponentPropertyFlags.NotSaved | ComponentPropertyFlags.Derived)) != 0)
            {
                left.Add(name);
                continue;
            }

            if (property.Nested is { } nested)
            {
                CollectUnsaved(nested.Properties, name, left);
            }
        }
    }

    private static string ComponentName(Type type) => type.IsGenericType ? type.Name[..type.Name.IndexOf('`')] : type.Name;

    private static void AssertSameJson(string expected, string actual)
    {
        if (expected == actual)
        {
            return;
        }

        var at = 0;
        while (at < expected.Length && at < actual.Length && expected[at] == actual[at])
        {
            at++;
        }

        var start = System.Math.Max(0, at - 200);
        Assert.Fail($"The restored entities read back differently, from character {at}:\nexpected ...{expected[start..System.Math.Min(expected.Length, at + 200)]}\nactual   ...{actual[start..System.Math.Min(actual.Length, at + 200)]}");
    }

    private static List<int> BuiltEntities(GameBuildPassResult pass)
    {
        var entityIds = new List<int>();
        for (var entityId = 0; entityId < pass.EcsContext.EntityManager.Capacity; entityId++)
        {
            if (pass.EcsContext.EntityManager.EntityExists(entityId) && !pass.Context.EntityFactory.Skeletons.IsSkeleton(entityId))
            {
                entityIds.Add(entityId);
            }
        }

        return entityIds;
    }

    private static GameBuildPassResult BuildPopulatedWorld()
    {
        var pass = BuiltInTestModules.Build(new Map(WorldBounds), sessionSeed: 1, initialEntityCapacity: 5000, initialComponentCapacity: 5000, crawlerNumbers: new UniqueNumberAllocator(1, 1, 24));
        var context = pass.Context;
        var playerEntityId = FloorBuilder.ReservePlayerEntity(pass.EcsContext);
        FloorBuilder.PopulateFloor(pass.World, pass.EcsContext, new NeighborhoodRecords(worldSeed: 1, floorNumber: FloorBuilder.FirstFloorNumber), pass.Factory, context.Terrain, context.Auras, context.Definitions, context.Areas, context.UniqueEntities);
        FloorBuilder.CreatePlayer(pass.World, pass.EcsContext, sessionSeed: 1, pass.Factory, context.Definitions, playerEntityId, FloorBuilder.PlayerSpawnOrigin(context.Areas));
        pass.World.PlayerEntityId = playerEntityId;
        Simulate(pass, 3 * FramesPerSecond);
        return pass;
    }

    private static GameBuildPassResult BuildEmptyWorldAt(long frame, Vector3Int reference)
    {
        var pass = BuiltInTestModules.Build(new Map(WorldBounds), sessionSeed: 1, initialEntityCapacity: 5000, initialComponentCapacity: 5000);
        pass.Context.ProcessingTierResolver.SetReferencePosition(reference);
        Simulate(pass, (int)frame);
        return pass;
    }

    private static void Simulate(GameBuildPassResult pass, int frames)
    {
        var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / FramesPerSecond);
        var start = pass.Context.SimulationClock.CurrentFrame;
        for (var frame = start + 1; frame <= start + frames; frame++)
        {
            pass.EcsContext.SystemManager.Update(new EngineTime(frameDuration * frame, frameDuration, false, frame));
        }
    }
}

/// <summary>A described component whose meaning no persistence knows, for the check.</summary>
[DescribedComponent]
public partial struct SnapshotFixtureComponent
{
    [PropertyMeaning("NoSuchMeaning")]
    public int Unpersisted { get; set; }
}
