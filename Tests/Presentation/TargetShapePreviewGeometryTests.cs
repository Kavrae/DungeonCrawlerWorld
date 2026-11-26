using Engine.Tiles;
using Game.Modules.Actions;
using Microsoft.Xna.Framework;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>An activation preview shows where a spec can be aimed (its reach) and the tiles it lands on, aimed at the far edge of that reach.</summary>
[TestClass]
public sealed class TargetShapePreviewGeometryTests
{
    [TestMethod]
    public void ASingleTargetSpell_LandsOnOneTileAtTheEdgeOfItsRange_AndReachesItsWholeRange()
    {
        var torch = TestContent.Action(BuiltInActionIds.Torch).Activator.Targeting;

        var landsOn = TargetShapePreviewGeometry.ComputeOffsets(torch);
        var reach = TargetShapePreviewGeometry.ComputeReach(torch);

        Assert.AreEqual(new Point(0, -torch.MaximumRange), Assert.ContainsSingle(landsOn));
        Assert.Contains(new Point(torch.MaximumRange, 0), reach);
        Assert.Contains(new Point(0, -torch.MaximumRange), reach);
        Assert.DoesNotContain(new Point(torch.MaximumRange + 1, 0), reach);
    }

    [TestMethod]
    public void AShapeLaidAroundTheCaster_ReachesExactlyTheTilesItLandsOn()
    {
        var adjacent = new TargetingSpec(TargetShape.Adjacent, MaximumRange: 0, Anchor: TargetAnchor.Caster);

        CollectionAssert.AreEquivalent(TargetShapePreviewGeometry.ComputeOffsets(adjacent), TargetShapePreviewGeometry.ComputeReach(adjacent));
    }
}
