using Engine.Math;
using Game.Modules.Abilities;

namespace Tests.Modules.Abilities;

[TestClass]
public sealed class TargetPriorityTests
{
    [TestMethod]
    public void SelectAutoTarget_NoCandidates_ReturnsNull()
    {
        var result = TargetPriority.SelectAutoTarget(
            attackerPosition: new Vector3Int(0, 0, 0),
            cursorTile: new Vector3Int(0, 0, 0),
            candidateTiles: []);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void SelectAutoTarget_PicksTheCandidateClosestToTheCursor()
    {
        var attacker = new Vector3Int(0, 0, 0);
        var cursor = new Vector3Int(10, 0, 0);
        var nearCursor = new Vector3Int(9, 0, 0);
        var nearAttacker = new Vector3Int(1, 0, 0);

        var result = TargetPriority.SelectAutoTarget(attacker, cursor, [nearAttacker, nearCursor]);

        Assert.AreEqual(nearCursor, result);
    }

    [TestMethod]
    public void SelectAutoTarget_TiedOnCursorDistance_BreaksTieByDistanceToAttacker()
    {
        var attacker = new Vector3Int(0, 0, 0);
        var cursor = new Vector3Int(5, 0, 0);

        // Both candidates are Manhattan distance 5 from the cursor, but candidateCloserToAttacker
        // is only 3 from the attacker versus candidateFartherFromAttacker's 7.
        var candidateCloserToAttacker = new Vector3Int(3, 0, 0);
        var candidateFartherFromAttacker = new Vector3Int(0, 10, 0);

        var result = TargetPriority.SelectAutoTarget(attacker, cursor, [candidateFartherFromAttacker, candidateCloserToAttacker]);

        Assert.AreEqual(candidateCloserToAttacker, result);
    }

    [TestMethod]
    public void SelectAutoTarget_CursorEqualsAttacker_DegeneratesToClosestToAttacker()
    {
        var attacker = new Vector3Int(0, 0, 0);
        var near = new Vector3Int(1, 0, 0);
        var far = new Vector3Int(5, 0, 0);

        var result = TargetPriority.SelectAutoTarget(attacker, cursorTile: attacker, [far, near]);

        Assert.AreEqual(near, result);
    }
}
