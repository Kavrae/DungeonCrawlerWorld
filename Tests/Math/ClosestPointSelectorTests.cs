using Engine.Math;

namespace Tests.Math;

[TestClass]
public sealed class ClosestPointSelectorTests
{
    [TestMethod]
    public void SelectClosest_NoCandidates_ReturnsNull()
    {
        var result = ClosestPointSelector.SelectClosest(
            primary: new Vector3Int(0, 0, 0),
            secondary: new Vector3Int(0, 0, 0),
            candidates: []);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void SelectClosest_PicksTheCandidateClosestToThePrimaryPoint()
    {
        var primary = new Vector3Int(10, 0, 0);
        var secondary = new Vector3Int(0, 0, 0);
        var nearPrimary = new Vector3Int(9, 0, 0);
        var nearSecondary = new Vector3Int(1, 0, 0);

        var result = ClosestPointSelector.SelectClosest(primary, secondary, [nearSecondary, nearPrimary]);

        Assert.AreEqual(nearPrimary, result);
    }

    [TestMethod]
    public void SelectClosest_TiedOnPrimaryDistance_BreaksTieByDistanceToSecondary()
    {
        var primary = new Vector3Int(5, 0, 0);
        var secondary = new Vector3Int(0, 0, 0);

        // Both candidates are Manhattan distance 3 from primary, but closerToSecondary is only
        // 2 from secondary versus fartherFromSecondary's 8.
        var closerToSecondary = new Vector3Int(2, 0, 0);
        var fartherFromSecondary = new Vector3Int(5, 3, 0);

        var result = ClosestPointSelector.SelectClosest(primary, secondary, [fartherFromSecondary, closerToSecondary]);

        Assert.AreEqual(closerToSecondary, result);
    }

    [TestMethod]
    public void SelectClosest_PrimaryEqualsSecondary_DegeneratesToClosestPoint()
    {
        var point = new Vector3Int(0, 0, 0);
        var near = new Vector3Int(1, 0, 0);
        var far = new Vector3Int(5, 0, 0);

        var result = ClosestPointSelector.SelectClosest(point, secondary: point, [far, near]);

        Assert.AreEqual(near, result);
    }
}
