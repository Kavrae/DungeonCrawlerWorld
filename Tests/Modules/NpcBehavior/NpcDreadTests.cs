using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Parts;
using Game.Blueprints.Races;
using Game.Modules.Actions.Activators;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.ItemPiles.Components;
using Game.Modules.Movement.Components;
using Game.Modules.NpcBehavior;
using Game.Modules.NpcBehavior.Behaviors;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using static Tests.Modules.NpcBehavior.NpcDecisionSystemTests;
using static Tests.Modules.NpcBehavior.NpcFleeTests;

namespace Tests.Modules.NpcBehavior;

/// <summary>Phase 6 of the NPC behavior plan: drives scaled at runtime by stat modifiers, and the Dread Idol's fear.</summary>
[TestClass]
public sealed class NpcDreadTests
{
    private const float DreadMagnitude = 3f;

    private static readonly Vector3Int PlayerPosition = Beside(CreaturePosition);

    private static int StationaryGoblin(Session session, Vector3Int position)
    {
        var goblin = Spawn(session, Goblin.Id, position);
        session.Components.GetPackedPool<MovementComponent>().Remove(goblin);
        return goblin;
    }

    private static MultiComponentPool<StatModifierComponent> StatModifiers(Session session) => session.Components.GetMultiPool<StatModifierComponent>();

    /// <summary>How many Dread Caution modifiers entityId holds, from the sum of their multiplicative magnitudes.</summary>
    private static int CautionModifiersOn(Session session, int entityId)
    {
        StatModifierMath.GetSums(StatModifiers(session), entityId, StatModifierTarget.BehaviorCaution, out _, out var multiplicativeSum);
        return (int)MathF.Round(multiplicativeSum / DreadMagnitude);
    }

    private static void FearPermanently(Session session, int entityId) =>
        StatModifierEffects.Apply(session.Components, entityId, StatModifierTarget.BehaviorCaution, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff,
            canModify: false, DreadMagnitude, FrameDeadline.Never, ActionSource.Admin);

    private static List<InventoryItemStackComponent> DreadIdolStacks(Session session, int entityId)
    {
        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(session.Components.GetMultiPool<InventoryItemStackComponent>(), entityId, stacks);
        return stacks.Where(stack => stack.ItemDefinitionId == BuiltInItemIds.DreadIdol).ToList();
    }

    private static bool IsLit(InventoryItemStackComponent stack) => stack.Override?.Activator is ToggleItemActivator { IsToggledOn: true };

    private static void RunUntil(Session session, Func<bool> condition, int maximumFrames = 10 * FramesPerSecond)
    {
        for (var frame = 0; frame < maximumFrames && !condition(); frame++)
        {
            session.RunFrames(1);
        }

        Assert.IsTrue(condition(), "the condition held within the frames allowed");
    }

    private static void Use(Session session, uint stackInstanceId, Game.Modules.Actions.TargetSelection selection)
    {
        RunUntil(session, () => !ActionLockGate.IsBlocked(session.Components.GetPackedPool<ActionLockComponent>(), session.PlayerId, session.Build.Context.SimulationClock.CurrentFrame));
        session.Components.Merge(session.PlayerId, new PendingItemActivationComponent(stackInstanceId, selection));
    }

    private static void LightCarried(Session session)
    {
        Use(session, DreadIdolStacks(session, session.PlayerId).Single().StackInstanceId, TestSelections.TargetModeAt(PlayerPosition));
        RunUntil(session, () => DreadIdolStacks(session, session.PlayerId).Any(IsLit));
    }

    private static void PutOutCarried(Session session)
    {
        Use(session, DreadIdolStacks(session, session.PlayerId).Single(IsLit).StackInstanceId, TestSelections.TargetModeAt(PlayerPosition));
        RunUntil(session, () => !DreadIdolStacks(session, session.PlayerId).Any(IsLit));
    }

    private static int PutDownLit(Session session)
    {
        Use(session, DreadIdolStacks(session, session.PlayerId).Single().StackInstanceId, TestSelections.At(PlayerPosition));
        var piles = session.Components.GetPackedPool<ItemPileComponent>();
        RunUntil(session, () => session.Build.World.GetOccupantEntityIdSpanAt(PlayerPosition).ToArray().Any(piles.Has));
        var pile = session.Build.World.GetOccupantEntityIdSpanAt(PlayerPosition).ToArray().Single(piles.Has);
        Assert.IsTrue(DreadIdolStacks(session, pile).Any(IsLit), "the idol lies in the pile, lit");
        return pile;
    }

    private static float FleeShare(Session session, int entityId) =>
        SampleChoices(session, entityId).GetValueOrDefault(BuiltInBehaviorIds.Flee) / (float)Trials;

    [TestMethod]
    public void TimedCautionModifier_RaisesFleesShare_AndLeavesNothingOnceItExpires()
    {
        var session = BuildSession(playerPosition: PlayerPosition);
        var goblin = Spawn(session, Goblin.Id, CreaturePosition);
        var unafraid = FleeShare(session, goblin);

        StatModifierEffects.Apply(session.Components, goblin, StatModifierTarget.BehaviorCaution, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff,
            canModify: false, DreadMagnitude, FrameDeadline.After(session.Build.Context.SimulationClock.CurrentFrame, FramesPerSecond), ActionSource.Admin);
        var afraid = FleeShare(session, goblin);

        Assert.IsTrue(unafraid is > 0.1f and < 0.4f, $"0.3 against Engage's 1 is ~23%, not {unafraid:P0}");
        Assert.IsTrue(afraid is > 0.4f and < 0.7f, $"0.3 x 4 against Engage's 1 is ~55%, not {afraid:P0}");

        session.RunFrames(2 * FramesPerSecond);

        Assert.AreEqual(0, CautionModifiersOn(session, goblin));
    }

    [TestMethod]
    public void LitCarriedIdol_HoldsOneRefreshedCautionModifierOnGoblinsInReach_AndNoneOnThePlayer()
    {
        var session = BuildSession(playerPosition: PlayerPosition);
        var twoAway = StationaryGoblin(session, PlayerPosition with { Y = PlayerPosition.Y + 2 });
        var threeAway = StationaryGoblin(session, PlayerPosition with { Y = PlayerPosition.Y + 3 });
        var outOfReach = StationaryGoblin(session, PlayerPosition with { Y = PlayerPosition.Y + 8 });

        LightCarried(session);
        session.RunFrames(4 * FramesPerSecond);

        Assert.AreEqual(1, CautionModifiersOn(session, twoAway));
        Assert.AreEqual(1, CautionModifiersOn(session, threeAway));
        Assert.AreEqual(0, CautionModifiersOn(session, outOfReach));
        Assert.AreEqual(0, CautionModifiersOn(session, session.PlayerId), "a carried source leaves its holder alone");
    }

    [TestMethod]
    public void IdolPutDownLit_ReachesThePlayerToo()
    {
        var session = BuildSession(playerPosition: PlayerPosition);

        PutDownLit(session);
        session.RunFrames(3 * FramesPerSecond);

        Assert.AreEqual(1, CautionModifiersOn(session, session.PlayerId));
    }

    [TestMethod]
    public void IdolPutOut_FearLapsesWithinTwoSeconds()
    {
        var session = BuildSession(playerPosition: PlayerPosition);
        var goblin = StationaryGoblin(session, PlayerPosition with { Y = PlayerPosition.Y + 2 });
        LightCarried(session);
        session.RunFrames(2 * FramesPerSecond);
        Assert.AreEqual(1, CautionModifiersOn(session, goblin), "Precondition: the goblin is afraid.");

        PutOutCarried(session);
        session.RunFrames(2 * FramesPerSecond + 5);

        Assert.AreEqual(0, CautionModifiersOn(session, goblin));
    }

    [TestMethod]
    public void GoblinThatShrugsOffDebuffs_StaysAfraidBetweenTicks_BecauseTheFearsDurationIsFixed()
    {
        var session = BuildSession(playerPosition: PlayerPosition);
        var goblin = StationaryGoblin(session, PlayerPosition with { Y = PlayerPosition.Y + 2 });
        StatModifierEffects.Apply(session.Components, goblin, StatModifierTarget.IncomingDebuffDuration, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, -0.75f, FrameDeadline.Never, ActionSource.Admin);
        LightCarried(session);
        RunUntil(session, () => CautionModifiersOn(session, goblin) == 1);

        var framesUnafraid = 0;
        session.RunFrames(4 * FramesPerSecond, () => framesUnafraid += CautionModifiersOn(session, goblin) == 0 ? 1 : 0);

        Assert.AreEqual(0, framesUnafraid);
    }

    [TestMethod]
    public void AfraidUnhurtLoneGoblin_FleesAboutHalfTheTime()
    {
        var session = BuildSession(playerPosition: PlayerPosition);
        var goblin = Spawn(session, Goblin.Id, CreaturePosition);
        FearPermanently(session, goblin);

        var share = FleeShare(session, goblin);

        Assert.IsTrue(share is > 0.4f and < 0.7f, $"0.3 x 4 against Engage's 1 is ~55%, not {share:P0}");
    }

    [TestMethod]
    public void AfraidUnhurtGoblinInAPackOfFour_HoldsItsGround()
    {
        var session = BuildSession(playerPosition: PlayerPosition);
        var goblin = Spawn(session, Goblin.Id, CreaturePosition);
        SpawnPackOfFour(session);
        FearPermanently(session, goblin);

        Assert.AreEqual(0f, FleeShare(session, goblin), "0.3 x 4 / 3 = 0.4 against Engage's 2 is under the cutoff");
    }

    [TestMethod]
    public void AfraidHurtGoblinInAPackOfFour_FleesAboutHalfTheTime()
    {
        var session = BuildSession(playerPosition: PlayerPosition);
        var goblin = Spawn(session, Goblin.Id, CreaturePosition);
        SpawnPackOfFour(session);
        HurtToJustOverHalf(session, goblin);
        FearPermanently(session, goblin);

        var share = FleeShare(session, goblin);

        Assert.IsTrue(share is > 0.35f and < 0.7f, $"~2.2 against Engage's 2 is about even, not {share:P0}");
    }

    [TestMethod]
    public void AfraidHurtBossAlone_FleesAboutHalfTheTime()
    {
        var session = BuildSession(playerPosition: PlayerPosition);
        var goblin = Spawn(session, Goblin.Id, CreaturePosition);
                session.Build.Factory.Apply(goblin, session.Build.Context.Definitions.GetId(Boss.Id));
        HurtToJustOverHalf(session, goblin);
        FearPermanently(session, goblin);

        var share = FleeShare(session, goblin);

        Assert.IsTrue(share is > 0.3f and < 0.65f, $"~1.65 against Engage's 2 is about even, not {share:P0}");
    }

    [TestMethod]
    public void UnhurtBoss_ReadsFullHealth_ThroughItsDoubledMaximum()
    {
        var session = BuildSession();
        var goblin = Spawn(session, Goblin.Id, CreaturePosition);
        session.Build.Factory.Apply(goblin, session.Build.Context.Definitions.GetId(Boss.Id));
        var services = new NpcDecisionServices(session.Build.Context, session.Components, behaviorStreamSeed: 1);
        var transform = session.Components.GetDirectPool<TransformComponent>().GetReadonly(goblin);

        var context = new NpcDecisionContext(services, goblin, session.Build.Context.SimulationClock.CurrentFrame, in transform, canMove: true, decisionSeed: 1, isActing: false);

        Assert.AreEqual(1f, context.HealthFraction(), 0.001f);
    }

    private static void SpawnPackOfFour(Session session)
    {
        for (var ally = 0; ally < 4; ally++)
        {
            Spawn(session, Goblin.Id, new Vector3Int(CreaturePosition.X - 2 + ally, CreaturePosition.Y + 4, GroundLayer));
        }
    }
}
