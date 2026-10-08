using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Races;
using Game.Blueprints.Composites;
using Game.Bootstrap;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.Spells;
using Game.Modules.Core.Components;
using Game.Modules.Mana.Components;
using Game.Modules.Movement.Components;
using Game.Spawning;
using Game.World;

namespace Tests.Modules.NpcBehavior;

/// <summary>TEMPORARY content, like TestCombatBehaviorSystem itself: Fairies wind up Magic Missile at the player, in Target or Ground mode at random.</summary>
[TestClass]
public sealed class FairyMagicMissileTests
{
    private const int FramesPerSecond = 60;

    private static readonly Vector3Int FairyPosition = new(10, 10, (int)MapLayer.Flying);

    private sealed record Session(GameBuildPassResult Build, int FairyId, int PlayerId)
    {
        public ComponentManager Components => Build.EcsContext.ComponentManager;

        /// <summary>The targeting mode of every Magic Missile windup the Fairy started over frameCount frames, in order.</summary>
        public List<TargetingMode> RunCollectingCasts(int frameCount)
        {
            var windups = Components.GetPackedPool<PendingWindupComponent>();
            var casts = new List<TargetingMode>();
            var wasWindingUp = false;
            var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / FramesPerSecond);
            var start = Build.EcsContext.SystemManager.Clock.CurrentFrame + 1;
            for (var frame = start; frame < start + frameCount; frame++)
            {
                Build.EcsContext.SystemManager.Update(new EngineTime(frameDuration * frame, frameDuration, false, frame));
                var isWindingUp = windups.TryGetReadonly(FairyId, out var windup) && windup.Activatable == ActivatableReference.Action(MagicMissileAction.Id);
                if (isWindingUp && !wasWindingUp)
                {
                    casts.Add(windup.Selection.Mode);
                }

                wasWindingUp = isWindingUp;
            }

            return casts;
        }
    }

    /// <param name="playerOffset">Where the player stands from the Fairy, on the ground below it.</param>
    private static Session BuildSession(Vector3Int playerOffset, float? fairyMana = null)
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(40, 40, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 200, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(FairyPosition);
        var definitions = build.Context.Definitions;

        var playerPosition = new Vector3Int(FairyPosition.X + playerOffset.X, FairyPosition.Y + playerOffset.Y, (int)MapLayer.Ground);
        var playerId = build.Factory.Spawn(SpawnRequest.At(definitions.GetId(Player.Id), playerPosition) with { Seed = 1 });
        build.EcsContext.ComponentManager.GetPackedPool<MovementComponent>().Remove(playerId);
        build.World.PlayerEntityId = playerId;

        var fairyId = build.Factory.Spawn(SpawnRequest.At(definitions.GetId(Fairy.Id), FairyPosition) with { Seed = 1 });
        var components = build.EcsContext.ComponentManager;
        components.GetPackedPool<MovementComponent>().TryUpdate(fairyId, static (ref MovementComponent movement) => movement.MovementMode = MovementMode.Random);
        if (fairyMana is { } mana)
        {
            components.GetPackedPool<ManaComponent>().TryUpdate(fairyId, mana, static (ref ManaComponent component, float value) => component.CurrentMana = value);
        }

        return new Session(build, fairyId, playerId);
    }

    [TestMethod]
    public void Fairy_HasFifteenManaAndADelayedRangeEightMagicMissile()
    {
        var session = BuildSession(new Vector3Int(4, 0, 0));

        Assert.AreEqual(15f, session.Components.GetPackedPool<ManaComponent>().GetReadonly(session.FairyId).MaximumMana);
        Assert.IsTrue(EntityActions.For(session.Components, session.Build.Context.Actions, session.Build.Context.Definitions).TryGetEffectiveAction(session.FairyId, MagicMissileAction.Id, out var magicMissile));
        Assert.AreEqual(ActionTimingCategory.Delayed, magicMissile.Activator.Timing.Category);
        Assert.AreEqual(8, magicMissile.Activator.Targeting.Range);
    }

    [TestMethod]
    public void PlayerInRangeAndNotAdjacent_FairyWindsUpMagicMissileAtThem()
    {
        var session = BuildSession(new Vector3Int(4, 0, 0));

        Assert.IsNotEmpty(session.RunCollectingCasts(3 * FramesPerSecond));
    }

    [TestMethod]
    public void PlayerAdjacent_FairyDoesNotCast()
    {
        var session = BuildSession(new Vector3Int(1, 0, 0));

        Assert.IsEmpty(session.RunCollectingCasts(3 * FramesPerSecond));
    }

    [TestMethod]
    public void PlayerOutOfRange_FairyDoesNotCast()
    {
        var session = BuildSession(new Vector3Int(9, 0, 0));

        Assert.IsEmpty(session.RunCollectingCasts(3 * FramesPerSecond));
    }

    [TestMethod]
    public void WithoutTheManaForIt_FairyNeverCasts()
    {
        var session = BuildSession(new Vector3Int(4, 0, 0), fairyMana: 0);

        Assert.IsEmpty(session.RunCollectingCasts(3 * FramesPerSecond));
    }

    [TestMethod]
    public void OutOfMana_FairyStopsCasting()
    {
        var session = BuildSession(new Vector3Int(4, 0, 0));

        var casts = session.RunCollectingCasts(20 * FramesPerSecond);

        Assert.HasCount(3, casts, "15 mana is three Magic Missiles; regeneration doesn't buy a fourth in twenty seconds.");
    }

    [TestMethod]
    public void TheSameSeed_CastsInTheSameModes()
    {
        var first = BuildSession(new Vector3Int(4, 0, 0)).RunCollectingCasts(10 * FramesPerSecond);
        var second = BuildSession(new Vector3Int(4, 0, 0)).RunCollectingCasts(10 * FramesPerSecond);

        Assert.IsNotEmpty(first);
        CollectionAssert.AreEqual(first, second);
    }
}
