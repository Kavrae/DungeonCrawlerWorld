using Engine.Math;
using Game.Blueprints.Races;
using Game.Modules.Core.Components;
using Game.Modules.NpcBehavior;
using Game.Spawning;
using static Tests.GameTests.Modules.NpcBehavior.NpcDecisionSystemTests;

namespace Tests.GameTests.Modules.NpcBehavior;

[TestClass]
public sealed class NpcAdjacencyTests
{
    [TestMethod]
    [DataRow((byte)1)]
    [DataRow((byte)3)]
    public void HostileOnEveryTileOfTheRing_IsAdjacent(byte footprintSide)
    {
        var (session, goblin) = BuildGoblin(footprintSide);

        for (var y = CreaturePosition.Y - 1; y <= CreaturePosition.Y + footprintSide; y++)
        {
            for (var x = CreaturePosition.X - 1; x <= CreaturePosition.X + footprintSide; x++)
            {
                var tile = new Vector3Int(x, y, GroundLayer);
                if (IsInFootprint(tile, footprintSide))
                {
                    continue;
                }

                Assert.IsTrue(HasHostileAdjacentWith(session, goblin, tile), $"A hostile at {tile} is beside the footprint.");
            }
        }
    }

    [TestMethod]
    [DataRow((byte)1)]
    [DataRow((byte)3)]
    public void HostileSharingTheFootprint_IsNotAdjacent(byte footprintSide)
    {
        var (session, goblin) = BuildGoblin(footprintSide);

        for (var y = CreaturePosition.Y; y < CreaturePosition.Y + footprintSide; y++)
        {
            for (var x = CreaturePosition.X; x < CreaturePosition.X + footprintSide; x++)
            {
                var tile = new Vector3Int(x, y, GroundLayer);
                Assert.IsFalse(HasHostileAdjacentWith(session, goblin, tile), $"A hostile at {tile} shares the footprint.");
            }
        }
    }

    [TestMethod]
    [DataRow((byte)1)]
    [DataRow((byte)3)]
    public void HostileTwoTilesAway_IsNotAdjacent(byte footprintSide)
    {
        var (session, goblin) = BuildGoblin(footprintSide);
        var tile = new Vector3Int(CreaturePosition.X + footprintSide + 1, CreaturePosition.Y, GroundLayer);

        Assert.IsFalse(HasHostileAdjacentWith(session, goblin, tile));
    }

    private static (Session Session, int Goblin) BuildGoblin(byte footprintSide)
    {
        var session = BuildSession();
        var blueprintId = session.Build.Context.Definitions.GetId(Goblin.Id);
        var goblin = session.Build.Factory.Spawn(SpawnRequest.At(blueprintId, CreaturePosition) with { Seed = 1, Size = new Vector2Byte(footprintSide, footprintSide) });
        return (session, goblin);
    }

    private static bool IsInFootprint(Vector3Int tile, byte footprintSide) =>
        tile.X >= CreaturePosition.X && tile.X < CreaturePosition.X + footprintSide &&
        tile.Y >= CreaturePosition.Y && tile.Y < CreaturePosition.Y + footprintSide;

    private static bool HasHostileAdjacentWith(Session session, int goblin, Vector3Int hostileTile)
    {
        var map = session.Build.World.Map;
        map.AddOccupantEntityId(hostileTile, session.PlayerId);
        var services = new NpcDecisionServices(session.Build.Context, session.Components, behaviorStreamSeed: 1);
        var transform = session.Components.GetDirectPool<TransformComponent>().GetReadonly(goblin);
        var context = new NpcDecisionContext(services, goblin, session.Build.Context.SimulationClock.CurrentFrame, in transform, canMove: true, decisionSeed: 1, isActing: false);
        var isAdjacent = context.HasHostileAdjacent();
        map.RemoveOccupantEntityId(hostileTile, session.PlayerId);
        return isAdjacent;
    }
}
