using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Spawning;
using Game.Terrain;
using Game.Views;
using Game.World;
using Presentation.UI.Content;

namespace Tests.ComponentProperties;

/// <summary>Admin inspection's rows, read through the property tables: meanings resolved, ActionSource as one row, health's derived rows, body parts named by race, one block per instance.</summary>
[TestClass]
public sealed class ComponentInspectionViewTests
{
    private static GameSession Bootstrap() =>
        GameBootstrapper.Build(ValidatedMods.None, new Map(new Vector3Int(20, 20, 3)), TestContent.Layers, sessionSeed: 1, initialEntityCapacity: 1_000, initialComponentCapacity: 100);

    private static List<InspectedPropertyRow> RowsOf<TComponent>(GameSession session, int entityId, int instance = 0)
    {
        var rows = new List<InspectedPropertyRow>();
        session.Views.ComponentInspection.CopyRows(entityId, typeof(TComponent), instance, rows);
        return rows;
    }

    private static string ValueOf(List<InspectedPropertyRow> rows, string label) =>
        rows.Single(row => row.Label == label).Value;

    [TestMethod]
    public void SimpleHealth_GainsItsModifiedMaximumAsADerivedRow()
    {
        var session = Bootstrap();
        var components = session.EcsContext.ComponentManager;
        var entityId = session.EcsContext.EntityManager.CreateEntity();
        components.GetPackedPool<SimpleHealthComponent>().Add(entityId, new SimpleHealthComponent(currentHealth: 120, baseMaximumHealth: 100));
        components.GetMultiPool<StatModifierComponent>().Add(entityId, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: 0.5f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));

        var rows = RowsOf<SimpleHealthComponent>(session, entityId);

        Assert.AreEqual("120", ValueOf(rows, "CurrentHealth"));
        Assert.AreEqual("100", ValueOf(rows, "BaseMaximumHealth"));
        Assert.AreEqual("150", ValueOf(rows, "MaximumHealth (modified)"), "Base 100 * 1.5.");
        Assert.IsTrue(rows.Single(row => row.Label == "MaximumHealth (modified)").IsReadOnly);
    }

    [TestMethod]
    public void BodyPartState_RowsAreTheRacesPartsByName()
    {
        var session = Bootstrap();
        var entityId = session.Internals.Factory.Spawn(SpawnRequest.At(session.Catalogs.Definitions.GetId(Goblin.Id), new Vector3Int(5, 5, (int)MapLayer.Ground)) with { Seed = 1 });
        session.Views.EntityBodyParts.Damage(entityId, partId: 0, amount: 5, effectiveMaximumHealth: 90, now: 0, lockoutFrames: 0);
        var parts = new List<Game.Modules.Health.BodyPartView>();
        foreach (var part in session.Views.EntityBodyParts.Parts(entityId))
        {
            parts.Add(part);
        }


        var rows = RowsOf<BodyPartStateComponent>(session, entityId);

        Assert.IsGreaterThan(0, parts.Count, "Precondition: a Goblin has body parts.");
        var healthRows = rows.SkipWhile(row => row.Label != "CurrentHealth").Skip(1).TakeWhile(row => row.Depth == 1).ToList();
        CollectionAssert.AreEqual(parts.Select(part => part.Name).ToList(), healthRows.Select(row => row.Label).ToList(), "One row a part the race has, by name, and none for unused slots: " + string.Join(" | ", rows.Select(row => $"{row.Depth}:{row.Label}={row.Value}")));
        Assert.IsTrue(rows.Any(row => row.Label == "MaximumHealth (modified)"));
    }

    [TestMethod]
    public void AFrame_ReadsAsSecondsFromNowAndNeverReadsAsNever()
    {
        var session = Bootstrap();
        var components = session.EcsContext.ComponentManager;
        var now = session.SimulationClock.CurrentFrame;
        var dodging = session.EcsContext.EntityManager.CreateEntity();
        components.GetPackedPool<DodgingComponent>().Add(dodging, new DodgingComponent((uint)(now + 2 * Engine.Utilities.GameTiming.FramesPerSecond)));
        var cooling = session.EcsContext.EntityManager.CreateEntity();
        components.GetMultiPool<ActionCooldownComponent>().Add(cooling, new ActionCooldownComponent(Guid.NewGuid(), FrameDeadline.Never));

        StringAssert.Contains(ValueOf(RowsOf<DodgingComponent>(session, dodging), "ExpiresAtFrame"), "(in 2.0 s)");
        Assert.AreEqual("never", ValueOf(RowsOf<ActionCooldownComponent>(session, cooling), "ReadyAtFrame"));
    }

    [TestMethod]
    public void AnActionSource_IsOneRowNamingTheSource_AndAKeyNamesItsEntity()
    {
        var session = Bootstrap();
        var components = session.EcsContext.ComponentManager;
        Assert.IsTrue(session.Catalogs.Terrain.TryGetId(BuiltInTerrain.LavaKey, out var lava));
        var owner = session.Internals.Factory.Spawn(SpawnRequest.At(session.Catalogs.Definitions.GetId(Goblin.Id), new Vector3Int(3, 3, (int)MapLayer.Ground)) with { Seed = 1 });
        var corpse = session.EcsContext.EntityManager.CreateEntity();
        components.GetPackedPool<DeadComponent>().Add(corpse, new DeadComponent(ActionSource.FromTerrain(lava), 10, session.EcsContext.ComponentManager.Keys.GetKey(owner)));

        var rows = RowsOf<DeadComponent>(session, corpse);

        Assert.AreEqual("terrain: Lava", ValueOf(rows, "KilledBy"));
        StringAssert.Contains(ValueOf(rows, "LootOwnerEntityKey"), $"(entity {owner})");
        Assert.IsFalse(rows.Any(row => row.Label is "Kind" or "TerrainTypeId"), "The source's parts aren't rows of their own.");
    }

    [TestMethod]
    public void EachInstance_IsItsOwnBlock_SortedByComponentName()
    {
        var session = Bootstrap();
        var components = session.EcsContext.ComponentManager;
        var entityId = session.EcsContext.EntityManager.CreateEntity();
        components.GetMultiPool<ActionCooldownComponent>().Add(entityId, new ActionCooldownComponent(Guid.NewGuid(), 5));
        components.GetMultiPool<ActionCooldownComponent>().Add(entityId, new ActionCooldownComponent(Guid.NewGuid(), 9));
        components.GetPackedPool<DodgingComponent>().Add(entityId, new DodgingComponent(3));
        var instances = new List<InspectedComponentInstance>();

        session.Views.ComponentInspection.CopyInstances(entityId, instances);

        CollectionAssert.AreEqual(new[] { "ActionCooldownComponent", "ActionCooldownComponent", "DodgingComponent" }, instances.Select(instance => instance.Name).ToArray());
        Assert.AreEqual(2, instances[0].InstanceCount);
        Assert.AreEqual("ActionCooldownComponent (2 of 2)", InspectionWindowContent.TitleOf(instances[1]));
        Assert.AreEqual("DodgingComponent", InspectionWindowContent.TitleOf(instances[2]));
        var readyAt = new[] { 0, 1 }.Select(instance => ValueOf(RowsOf<ActionCooldownComponent>(session, entityId, instance), "ReadyAtFrame")).ToList();
        Assert.IsTrue(readyAt.Exists(value => value.StartsWith("frame 5 ", StringComparison.Ordinal)) && readyAt.Exists(value => value.StartsWith("frame 9 ", StringComparison.Ordinal)), string.Join(", ", readyAt));
    }

    [TestMethod]
    public void AWrite_ChangesOnlyThatInstancesVersion()
    {
        var session = Bootstrap();
        var components = session.EcsContext.ComponentManager;
        var entityId = session.EcsContext.EntityManager.CreateEntity();
        components.GetMultiPool<ActionCooldownComponent>().Add(entityId, new ActionCooldownComponent(Guid.NewGuid(), 5));
        components.GetMultiPool<ActionCooldownComponent>().Add(entityId, new ActionCooldownComponent(Guid.NewGuid(), 9));
        var before = new List<InspectedComponentInstance>();
        var after = new List<InspectedComponentInstance>();
        session.Views.ComponentInspection.CopyInstances(entityId, before);

        using (var write = components.GetMultiPool<ActionCooldownComponent>().WriteByDenseIndex(components.GetMultiPool<ActionCooldownComponent>().GetNextDenseIndex(components.GetMultiPool<ActionCooldownComponent>().GetFirstDenseIndex(entityId))))
        {
            write.Value.ReadyAtFrame = 11;
        }

        session.Views.ComponentInspection.CopyInstances(entityId, after);
        Assert.AreEqual(before[0].Version, after[0].Version);
        Assert.AreEqual(before[1].Version + 1, after[1].Version);
    }

    [TestMethod]
    public void FormatRows_IndentsByDepthAndLeavesHeadingsWithoutAValue()
    {
        var text = InspectionWindowContent.FormatRows([
            new InspectedPropertyRow(0, "Selection", "", false),
            new InspectedPropertyRow(1, "Mode", "Target", false),
        ]);

        Assert.AreEqual("Selection\n  Mode : Target", text);
        Assert.AreEqual("(no properties)", InspectionWindowContent.FormatRows([]));
    }
}
