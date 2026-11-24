using Engine.Math;
using Game.Blueprints.Races;
using Game.Modules.Core.Components;
using Game.Modules.NpcBehavior;
using Game.Spawning;
using static Tests.GameTests.Modules.NpcBehavior.NpcDecisionSystemTests;

namespace Tests.GameTests.Modules.NpcBehavior;

[TestClass]
public sealed class NpcPerceptionRangeTests
{
    private const byte AllyRadius = 3;

    private static readonly Vector3Int AllyHome = new(35, 5, GroundLayer);

    [TestMethod]
    [DataRow((byte)1)]
    [DataRow((byte)3)]
    public void HostileExactlyTheSightRadiusAway_IsSeenOnEverySideAndCorner(byte footprintSide)
    {
        var (session, goblin) = BuildGoblin(footprintSide);

        foreach (var tile in TilesAtDistance(footprintSide, NpcDecisionContext.HostileSightRadius))
        {
            Assert.AreEqual(1, PerceiveWith(session, goblin, session.PlayerId, tile, (ref NpcDecisionContext context) => context.HostilesInSight()), $"A hostile at {tile} is {NpcDecisionContext.HostileSightRadius} tiles away.");
        }
    }

    [TestMethod]
    [DataRow((byte)1)]
    [DataRow((byte)3)]
    public void HostileOneTilePastTheSightRadius_IsNotSeenOnAnySideOrCorner(byte footprintSide)
    {
        var (session, goblin) = BuildGoblin(footprintSide);

        foreach (var tile in TilesAtDistance(footprintSide, NpcDecisionContext.HostileSightRadius + 1))
        {
            Assert.AreEqual(0, PerceiveWith(session, goblin, session.PlayerId, tile, (ref NpcDecisionContext context) => context.HostilesInSight()), $"A hostile at {tile} is past the sight radius.");
        }
    }

    [TestMethod]
    [DataRow((byte)1)]
    [DataRow((byte)3)]
    public void AllyExactlyTheRadiusAway_IsCountedOnEverySideAndCorner(byte footprintSide)
    {
        var (session, goblin) = BuildGoblin(footprintSide);
        var ally = session.Spawn(Goblin.Id, AllyHome);

        foreach (var tile in TilesAtDistance(footprintSide, AllyRadius))
        {
            Assert.AreEqual(1, PerceiveWith(session, goblin, ally, tile, (ref NpcDecisionContext context) => context.AlliesWithin(AllyRadius)), $"An ally at {tile} is {AllyRadius} tiles away.");
        }
    }

    [TestMethod]
    [DataRow((byte)1)]
    [DataRow((byte)3)]
    public void AllyOneTilePastTheRadius_IsNotCountedOnAnySideOrCorner(byte footprintSide)
    {
        var (session, goblin) = BuildGoblin(footprintSide);
        var ally = session.Spawn(Goblin.Id, AllyHome);

        foreach (var tile in TilesAtDistance(footprintSide, AllyRadius + 1))
        {
            Assert.AreEqual(0, PerceiveWith(session, goblin, ally, tile, (ref NpcDecisionContext context) => context.AlliesWithin(AllyRadius)), $"An ally at {tile} is past the radius.");
        }
    }

    private delegate int Perception(ref NpcDecisionContext context);

    private static (Session Session, int Goblin) BuildGoblin(byte footprintSide)
    {
        var session = BuildSession();
        var blueprintId = session.Build.Context.Definitions.GetId(Goblin.Id);
        var goblin = session.Build.Factory.Spawn(SpawnRequest.At(blueprintId, CreaturePosition) with { Seed = 1, Size = new Vector2Byte(footprintSide, footprintSide) });
        return (session, goblin);
    }

    /// <summary>The four tiles straight out from the footprint's sides and its four diagonal corners, each distance tiles from it.</summary>
    private static IEnumerable<Vector3Int> TilesAtDistance(byte footprintSide, int distance)
    {
        var left = CreaturePosition.X - distance;
        var right = CreaturePosition.X + footprintSide - 1 + distance;
        var top = CreaturePosition.Y - distance;
        var bottom = CreaturePosition.Y + footprintSide - 1 + distance;
        var middleX = CreaturePosition.X + footprintSide / 2;
        var middleY = CreaturePosition.Y + footprintSide / 2;
        foreach (var (x, y) in new[] { (left, middleY), (right, middleY), (middleX, top), (middleX, bottom), (left, top), (right, top), (left, bottom), (right, bottom) })
        {
            yield return new Vector3Int(x, y, GroundLayer);
        }
    }

    private static int PerceiveWith(Session session, int goblin, int otherEntityId, Vector3Int tile, Perception perceive)
    {
        var map = session.Build.World.Map;
        map.AddOccupantEntityId(tile, otherEntityId);
        var services = new NpcDecisionServices(session.Build.Context, session.Components, behaviorStreamSeed: 1);
        var transform = session.Components.GetDirectPool<TransformComponent>().GetReadonly(goblin);
        var context = new NpcDecisionContext(services, goblin, session.Build.Context.SimulationClock.CurrentFrame, in transform, canMove: true, decisionSeed: 1, isActing: false);
        var perceived = perceive(ref context);
        map.RemoveOccupantEntityId(tile, otherEntityId);
        return perceived;
    }
}
