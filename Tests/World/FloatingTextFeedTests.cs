using Engine.Events;
using Engine.Math;
using Game.Modules.ProcessingTier.Components;
using Game.World;

namespace Tests.World;

[TestClass]
public sealed class FloatingTextFeedTests
{
    [TestMethod]
    public void Publish_LocalEntity_PublishesWithItsFootprint()
    {
        var floatingText = new TestFloatingText().Place(3, ProcessingTierLevel.Local, x: 5, y: 7, layer: 1, width: 2, height: 2);

        floatingText.Feed.Publish(3, FloatingTextKind.DamageTaken, 12);

        Assert.HasCount(1, floatingText.Published);
        Assert.AreEqual(new FloatingTextEvent(3, FloatingTextKind.DamageTaken, 12, new Vector3Int(5, 7, 1), new Vector2Byte(2, 2)), floatingText.Published[0]);
    }

    [TestMethod]
    [DataRow(ProcessingTierLevel.Neighborhood)]
    [DataRow(ProcessingTierLevel.Borough)]
    [DataRow(ProcessingTierLevel.Beyond)]
    public void Publish_EntityOutsideLocalTier_PublishesNothing(ProcessingTierLevel tier)
    {
        var floatingText = new TestFloatingText().Place(3, tier);

        floatingText.Feed.Publish(3, FloatingTextKind.DamageTaken, 12);

        Assert.IsEmpty(floatingText.Published);
    }

    [TestMethod]
    public void Publish_UntieredEntity_PublishesNothing()
    {
        var floatingText = new TestFloatingText();

        floatingText.Feed.Publish(3, FloatingTextKind.DamageTaken, 12);

        Assert.IsEmpty(floatingText.Published);
    }
}
