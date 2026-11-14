using Engine.Decisions;
using Engine.ECS.Components;
using Engine.Math;
using Game.Blueprints.Composites;
using Game.Blueprints.NPCs.Generic;
using Game.Blueprints.Parts;
using Game.Blueprints.Races;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Movement.Components;
using Game.Modules.NpcBehavior;
using Game.Modules.NpcBehavior.Behaviors;
using Game.Modules.NpcBehavior.Components;
using Game.Spawning;
using Game.Terrain;
using Game.World;
using static Tests.Modules.NpcBehavior.NpcDecisionSystemTests;

namespace Tests.Modules.NpcBehavior;

/// <summary>Phase 5 of the NPC behavior plan: Boss's modifiers, pack courage and fear, and Flee with its commitment.</summary>
[TestClass]
public sealed class NpcFleeTests
{
    internal const int Trials = 400;

    internal static int Spawn(Session session, Guid blueprintId, Vector3Int position) =>
        session.Build.Factory.Spawn(SpawnRequest.At(session.Build.Context.Definitions.GetId(blueprintId), position) with { Seed = 1 });

    /// <summary>Hurts entityId's limbs to a little over half health: too hurt to stand its ground lightly, not hurt enough to heal.</summary>
    internal static void HurtToJustOverHalf(Session session, int entityId)
    {
        session.HurtUntil(entityId, 0.6f);
        var fraction = session.HealthFractionOf(entityId);
        Assert.IsTrue(fraction is >= 0.5f and <= 0.6f, $"the setup leaves it a little over half health, not {fraction}");
    }

    private static NpcDecisionServices Services(Session session) => new(session.Build.Context, session.Components, behaviorStreamSeed: 1);

    private static BehaviorSet<NpcDecisionContext> SetOf(Session session, int entityId) =>
        session.Build.Context.NpcDecisions.Sets.Get(session.Components.GetPackedPool<NpcDecisionComponent>().GetReadonly(entityId).BehaviorSetId);

    /// <summary>How often each behavior is chosen for entityId as it stands, over Trials decisions with different seeds and no commitment.</summary>
    internal static Dictionary<Guid, int> SampleChoices(Session session, int entityId)
    {
        var services = Services(session);
        var set = SetOf(session, entityId);
        var arbiter = new BehaviorArbiter<NpcDecisionContext>(new BehaviorArbiterOptions());
        var transform = session.Components.GetDirectPool<TransformComponent>().GetReadonly(entityId);
        var canMove = session.Components.GetPackedPool<MovementComponent>().Has(entityId);
        var now = session.Build.Context.SimulationClock.CurrentFrame + 10 * FramesPerSecond;
        var counts = new Dictionary<Guid, int>();
        for (var seed = 1UL; seed <= Trials; seed++)
        {
            var context = new NpcDecisionContext(services, entityId, now, in transform, canMove, seed, isActing: false);
            var choice = arbiter.Choose(ref context, set, default);
            var chosen = choice.IsNone ? Guid.Empty : set[choice.EntryIndex].Behavior.Id;
            counts[chosen] = counts.GetValueOrDefault(chosen) + 1;
        }

        return counts;
    }

    private static float ResolvedWeightOf(BehaviorSet<NpcDecisionContext> set, Guid behaviorId) =>
        set.Entries.ToArray().Single(entry => entry.Behavior.Id == behaviorId).ResolvedWeight;

    [TestMethod]
    public void GoblinForeman_IsBossThroughItsInclude_TwiceAsAggressiveAndAQuarterAsCautious()
    {
        var session = BuildSession();
        var foreman = Spawn(session, GoblinForeman.Id, CreaturePosition);

        var set = SetOf(session, foreman);

        Assert.AreEqual(2f, ResolvedWeightOf(set, BuiltInBehaviorIds.EngageMelee));
        Assert.AreEqual(3f * 0.25f, ResolvedWeightOf(set, BuiltInBehaviorIds.Flee));
    }

    [TestMethod]
    public void BossAppliedTwice_MultipliesOnce()
    {
        var session = BuildSession();
        var goblin = Spawn(session, Goblin.Id, CreaturePosition);
        var bossId = session.Build.Context.Definitions.GetId(Boss.Id);

        session.Build.Factory.Apply(goblin, bossId);
        session.Build.Factory.Apply(goblin, bossId);

        var set = SetOf(session, goblin);
        Assert.AreEqual(2f, ResolvedWeightOf(set, BuiltInBehaviorIds.EngageMelee));
        Assert.AreEqual(0.75f, ResolvedWeightOf(set, BuiltInBehaviorIds.Flee));
    }

    [TestMethod]
    public void AlliesWithin_CountsLivingAlliesOnItsLayerToTheRadiusEdge()
    {
        var session = BuildSession();
        var goblin = Spawn(session, Goblin.Id, CreaturePosition);
        Spawn(session, Goblin.Id, CreaturePosition with { X = CreaturePosition.X + 8 });
        Spawn(session, Goblin.Id, CreaturePosition with { Y = CreaturePosition.Y - 3 });
        Spawn(session, Goblin.Id, CreaturePosition with { X = CreaturePosition.X + 9 });
        Spawn(session, Fairy.Id, CreaturePosition with { X = CreaturePosition.X - 2, Z = (int)MapLayer.Flying });
        Spawn(session, Fairy.Id, CreaturePosition with { X = CreaturePosition.X - 3 });
        var deadAlly = Spawn(session, Goblin.Id, CreaturePosition with { Y = CreaturePosition.Y + 2 });
        session.Components.Merge(deadAlly, new Game.Modules.Death.Components.DeadComponent(ActionSource.Admin, 0));
        session.Build.Factory.Spawn(SpawnRequest.At(session.Build.Context.Definitions.GetId(Goblin.Id), CreaturePosition with { Y = CreaturePosition.Y + 1, Z = (int)MapLayer.UnderGround }) with { Seed = 1 });

        var transform = session.Components.GetDirectPool<TransformComponent>().GetReadonly(goblin);
        var context = new NpcDecisionContext(Services(session), goblin, 0, in transform, canMove: true, decisionSeed: 1, isActing: false);

        Assert.AreEqual(2, context.AlliesWithin(8), "the ally 8 tiles away and the one 3 tiles away; not the one 9 away, the dead one, the one on another layer, a fairy, or itself");
    }

    [TestMethod]
    public void HostilesInSight_LeavesOutAHostileBehindAWall()
    {
        var session = BuildSession(playerPosition: CreaturePosition with { X = CreaturePosition.X + 2 });
        var goblin = Spawn(session, Goblin.Id, CreaturePosition);
        var world = session.Build.World;
        world.SetStructure(CreaturePosition with { X = CreaturePosition.X + 1 }, new TerrainCell(world.Terrain.GetId(BuiltInTerrain.StoneWallKey)));
        var dummy = Spawn(session, TestDummyBlueprint.Id, CreaturePosition with { Y = CreaturePosition.Y + 2 });

        var transform = session.Components.GetDirectPool<TransformComponent>().GetReadonly(goblin);
        var context = new NpcDecisionContext(Services(session), goblin, 0, in transform, canMove: true, decisionSeed: 1, isActing: false);

        Assert.AreEqual(1, context.HostilesInSight());
        Assert.AreEqual(session.Build.Context.EntityKeys.GetKey(dummy), context.NearestHostileInSight());
    }

    [TestMethod]
    public void LoneHurtGoblinBesideAHostile_FleesAboutTwoThirdsOfTheTime()
    {
        var session = BuildSession(playerPosition: Beside(CreaturePosition));
        var goblin = Spawn(session, Goblin.Id, CreaturePosition);
        HurtToJustOverHalf(session, goblin);

        var choices = SampleChoices(session, goblin);

        var fleeShare = choices.GetValueOrDefault(BuiltInBehaviorIds.Flee) / (float)Trials;
        Assert.IsTrue(fleeShare is > 0.45f and < 0.8f, $"(0.1 + ~0.43) x 3 against Engage's 1 is ~62%, not {fleeShare:P0}");
        Assert.AreEqual(Trials, choices.GetValueOrDefault(BuiltInBehaviorIds.Flee) + choices.GetValueOrDefault(BuiltInBehaviorIds.EngageMelee));
    }

    [TestMethod]
    public void UnhurtLoneGoblinBesideAHostile_MostlyFights()
    {
        var session = BuildSession(playerPosition: Beside(CreaturePosition));
        var goblin = Spawn(session, Goblin.Id, CreaturePosition);

        var choices = SampleChoices(session, goblin);

        var fleeShare = choices.GetValueOrDefault(BuiltInBehaviorIds.Flee) / (float)Trials;
        Assert.IsTrue(fleeShare is > 0.1f and < 0.4f, $"0.1 x 3 against Engage's 1 is ~23%, not {fleeShare:P0}");
    }

    [TestMethod]
    public void HurtGoblinInAPackOfEight_NeverFlees()
    {
        var session = BuildSession(playerPosition: Beside(CreaturePosition));
        var goblin = Spawn(session, Goblin.Id, CreaturePosition);
        for (var ally = 0; ally < 8; ally++)
        {
            Spawn(session, Goblin.Id, new Vector3Int(CreaturePosition.X - 4 + ally, CreaturePosition.Y + 4, GroundLayer));
        }

        HurtToJustOverHalf(session, goblin);

        var choices = SampleChoices(session, goblin);

        Assert.AreEqual(0, choices.GetValueOrDefault(BuiltInBehaviorIds.Flee), "pack fear divides Flee by 5 and courage triples Engage, so Flee falls under the cutoff");
        Assert.AreEqual(Trials, choices.GetValueOrDefault(BuiltInBehaviorIds.EngageMelee));
    }

    [TestMethod]
    public void HurtBossGoblinAlone_NeverFlees()
    {
        var session = BuildSession(playerPosition: Beside(CreaturePosition));
        var goblin = Spawn(session, Goblin.Id, CreaturePosition);
                session.Build.Factory.Apply(goblin, session.Build.Context.Definitions.GetId(Boss.Id));
        HurtToJustOverHalf(session, goblin);

        var choices = SampleChoices(session, goblin);

        Assert.AreEqual(0, choices.GetValueOrDefault(BuiltInBehaviorIds.Flee));
    }

    [TestMethod]
    public void HurtStationaryCreatureBesideAHostile_NeverFlees()
    {
        var session = BuildSession(playerPosition: Beside(CreaturePosition));
        var stationary = Spawn(session, GoblinFairy.Id, CreaturePosition);
        HurtToJustOverHalf(session, stationary);

        var choices = SampleChoices(session, stationary);

        Assert.AreEqual(0, choices.GetValueOrDefault(BuiltInBehaviorIds.Flee));
    }

    [TestMethod]
    public void HostileTwoTilesAway_IsNoReasonToStartFleeing()
    {
        var session = BuildSession(playerPosition: CreaturePosition with { X = CreaturePosition.X + 2 });
        var goblin = Spawn(session, Goblin.Id, CreaturePosition);
        HurtToJustOverHalf(session, goblin);

        var choices = SampleChoices(session, goblin);

        Assert.AreEqual(0, choices.GetValueOrDefault(BuiltInBehaviorIds.Flee));
    }

    /// <summary>A hurt goblin beside a test dummy, inspected, run until it commits to fleeing from the dummy.</summary>
    private static (Session Session, int Goblin, int Dummy) FleeingGoblin()
    {
        var session = BuildSession();
        var goblin = Spawn(session, Goblin.Id, CreaturePosition);
        var dummy = Spawn(session, TestDummyBlueprint.Id, Beside(CreaturePosition));
        HurtToJustOverHalf(session, goblin);
        session.Build.Context.NpcDecisions.Inspect(goblin);

        var commitments = session.Components.GetPackedPool<BehaviorCommitment>();
        for (var frame = 0; frame < 10 * FramesPerSecond && !commitments.Has(goblin); frame++)
        {
            session.RunFrames(1);
        }

        Assert.IsTrue(commitments.Has(goblin), "a lone hurt goblin beside a hostile flees within ten seconds");
        return (session, goblin, dummy);
    }

    [TestMethod]
    public void FleeingGoblin_CommitsToRunningFromThatHostile_AndGetsFartherAway()
    {
        var (session, goblin, dummy) = FleeingGoblin();
        var commitment = session.Components.GetPackedPool<BehaviorCommitment>().GetReadonly(goblin);
        var startDistance = Distance(session.PositionOf(goblin), session.PositionOf(dummy));

        session.RunFrames(FramesPerSecond);

        Assert.AreEqual(session.Build.Context.EntityKeys.GetKey(dummy), commitment.Target);
        Assert.AreEqual(SetOf(session, goblin).Entries.ToArray().Single(entry => entry.Behavior.Id == BuiltInBehaviorIds.Flee).SessionId, commitment.BehaviorSessionId);
        Assert.IsGreaterThan(startDistance, Distance(session.PositionOf(goblin), session.PositionOf(dummy)));
    }

    [TestMethod]
    public void FleeingGoblin_StopsWhenWhatItRunsFromIsDestroyed()
    {
        var (session, goblin, dummy) = FleeingGoblin();
        var state = session.Build.Context.NpcDecisions;

        session.Build.EcsContext.EntityManager.DestroyEntity(dummy);
        var destroyedAt = session.Build.Context.SimulationClock.CurrentFrame;
        for (var frame = 0; frame < 3 * FramesPerSecond && state.InspectedDecisionFrame <= destroyedAt; frame++)
        {
            session.RunFrames(1);
        }

        Assert.IsGreaterThan(destroyedAt, state.InspectedDecisionFrame);
        Assert.AreNotEqual(DecisionPath.Continued, state.InspectedTrace.Path);
        Assert.AreNotEqual("Flee", state.InspectedChoice);
    }

    [TestMethod]
    public void FleeingGoblin_IsInterruptedBySelfHeal()
    {
        var (session, goblin, _) = FleeingGoblin();
        var state = session.Build.Context.NpcDecisions;
        InventoryActions.AddItem(session.Components, goblin, BuiltInItemIds.HealthPotion, 1);
        session.HurtBelowHalf(goblin);

        var hurtAt = session.Build.Context.SimulationClock.CurrentFrame;
        for (var frame = 0; frame < 3 * FramesPerSecond && state.InspectedDecisionFrame <= hurtAt; frame++)
        {
            session.RunFrames(1);
        }

        Assert.AreEqual("Self-heal", state.InspectedChoice);
        Assert.AreEqual(DecisionPath.Interrupt, state.InspectedTrace.Path);
    }

    private static int Distance(Vector3Int first, Vector3Int second) => System.Math.Max(System.Math.Abs(first.X - second.X), System.Math.Abs(first.Y - second.Y));
}
