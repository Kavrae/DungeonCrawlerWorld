using Engine.ECS.Components;
using Engine.Math;
using Game.Blueprints.Races;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Game.Spawning;
using Game.World;

namespace Tests.GameTests.Modules.Actions;

/// <summary>A grant of a disabled action is a grant of its replacement: an entity granted both has one action, listed once, with one cooldown, and the old Guid -- a hotkey bound to it -- uses the replacement.</summary>
[TestClass]
public sealed class DisabledActionGrantTests
{
    private const int Ground = (int)MapLayer.Ground;

    /// <summary>A built session where Quick Attack is disabled with Power Attack as its replacement -- as the content would load it -- and a goblin, whose race grants both.</summary>
    private static (Game.Bootstrap.GameBuildPassResult Build, EntityActions Actions, int GoblinId) BuildWithQuickAttackReplaced()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(20, 20, 3)), sessionSeed: 1, initialEntityCapacity: 100, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(new Vector3Int(5, 5, Ground));
        var catalog = build.Context.Actions;
        catalog.Get(BuiltInActionIds.PowerAttack);
        catalog.Remove(BuiltInActionIds.QuickAttack);
        catalog.AddRedirect(BuiltInActionIds.QuickAttack, BuiltInActionIds.PowerAttack);

        var goblinId = build.Factory.Spawn(SpawnRequest.At(build.Context.Definitions.GetId(Goblin.Id), new Vector3Int(5, 5, Ground)) with { Seed = 1 });
        build.EcsContext.ComponentManager.GetPackedPool<MovementComponent>().Remove(goblinId);
        return (build, EntityActions.For(build.EcsContext.ComponentManager, catalog, build.Context.Definitions), goblinId);
    }

    [TestMethod]
    public void ARaceGrantingADisabledActionAndItsReplacement_ListsTheReplacementOnce()
    {
        var (_, actions, goblinId) = BuildWithQuickAttackReplaced();
        var ids = new List<Guid>();

        actions.CopyActionIds(goblinId, ids);

        Assert.AreEqual(1, ids.Count(id => id == BuiltInActionIds.PowerAttack));
        Assert.DoesNotContain(BuiltInActionIds.QuickAttack, ids);
    }

    [TestMethod]
    public void TheDisabledActionsGuid_UsesTheReplacement_AsTheRaceGrantsIt()
    {
        var (_, actions, goblinId) = BuildWithQuickAttackReplaced();

        Assert.IsTrue(actions.TryGetEffectiveAction(goblinId, BuiltInActionIds.QuickAttack, out var throughOldGuid));
        Assert.IsTrue(actions.TryGetEffectiveAction(goblinId, BuiltInActionIds.PowerAttack, out var direct));

        Assert.AreEqual(BuiltInActionIds.PowerAttack, throughOldGuid.Id);
        Assert.AreSame(direct, throughOldGuid, "The race's own Power Attack grant -- naming it directly -- wins over the Quick Attack grant that resolves to it.");
    }

    [TestMethod]
    public void ACooldownStartedThroughEitherGuid_IsTheOneCooldown()
    {
        var (_, actions, goblinId) = BuildWithQuickAttackReplaced();

        actions.SetCooldown(goblinId, BuiltInActionIds.QuickAttack, cooldownFrames: 30, now: 0);

        Assert.IsTrue(actions.IsOnCooldown(goblinId, BuiltInActionIds.PowerAttack, now: 10));
    }

    [TestMethod]
    public void GrantingADisabledAction_GrantsItsReplacement_AndGrantingBothIsOneAction()
    {
        var (build, actions, _) = BuildWithQuickAttackReplaced();
        var componentManager = build.EcsContext.ComponentManager;
        var entityId = build.EcsContext.EntityManager.CreateEntity();

        ActionGrantEffects.Grant(componentManager, build.Context.Actions, entityId, BuiltInActionIds.QuickAttack, overrideDefinition: null);
        ActionGrantEffects.Grant(componentManager, build.Context.Actions, entityId, BuiltInActionIds.PowerAttack, overrideDefinition: null);

        var instances = componentManager.GetMultiPool<ActionInstanceComponent>();
        var granted = new List<Guid>();
        for (var denseIndex = instances.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = instances.GetNextDenseIndex(denseIndex))
        {
            granted.Add(instances.GetReadonlyByDenseIndex(denseIndex).ActionId);
        }

        Assert.IsTrue(granted.All(id => id == BuiltInActionIds.PowerAttack), $"Every grant names the replacement: {string.Join(", ", granted)}.");
        var listed = new List<Guid>();
        actions.CopyActionIds(entityId, listed);
        CollectionAssert.AreEqual(new[] { BuiltInActionIds.PowerAttack }, listed);
        Assert.IsTrue(actions.Has(entityId, BuiltInActionIds.QuickAttack));
    }

    [TestMethod]
    public void ResolveId_FollowsRedirectsToTheFirstRegisteredGuid_AndLeavesARegisteredOneAlone()
    {
        var (build, _, _) = BuildWithQuickAttackReplaced();
        var catalog = build.Context.Actions;

        Assert.AreEqual(BuiltInActionIds.PowerAttack, catalog.ResolveId(BuiltInActionIds.QuickAttack));
        Assert.AreEqual(BuiltInActionIds.Heal, catalog.ResolveId(BuiltInActionIds.Heal));
    }
}
