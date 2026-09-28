using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.StatusEffects;
using Game.World;
using Presentation.UI.Chrome;
using Presentation.UI.FloatingText;

namespace Tests.Presentation;

[TestClass]
public sealed class FloatingTextControllerTests
{
    private sealed class Harness
    {
        public EventBus EventBus { get; } = new();
        public SimulationClock Clock { get; } = new();
        public FloatingTextController Controller { get; }

        public Harness() => Controller = new FloatingTextController(EventBus, Clock, new Random(7));

        public void Publish(int entityId, FloatingTextKind kind = FloatingTextKind.DamageTaken, ushort amount = 1, int x = 10, int y = 20, int layer = 1, StatusEffectType effectType = default) =>
            EventBus.Publish(new FloatingTextEvent(entityId, kind, amount, new Vector3Int(x, y, layer), new Vector2Byte(1, 1), effectType));

        public void AdvanceFrames(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                Clock.Advance(Clock.CurrentFrame + 1);
                Controller.Update();
            }
        }

        public List<FloatingTextInstance> ActiveTexts()
        {
            var texts = new List<FloatingTextInstance>();
            for (var index = 0; index < Controller.ActiveTextCount; index++)
            {
                texts.Add(Controller.GetActiveText(index));
            }

            return texts;
        }
    }

    [TestMethod]
    public void Update_NextSimulatedFrame_ReleasesTheTextAboveItsEntity()
    {
        var harness = new Harness();
        harness.Publish(3, amount: 12, x: 10, y: 20, layer: 1);

        harness.AdvanceFrames(1);

        var text = harness.ActiveTexts().Single();
        Assert.AreEqual(12, text.Amount);
        Assert.AreEqual(1, text.MapLayer);
        Assert.AreEqual(20f, text.SpawnTilePosition.Y);
        Assert.IsGreaterThanOrEqualTo(10.5f - FloatingTextChrome.SpawnJitterTiles, text.SpawnTilePosition.X, "A number appears on the left half of its entity.");
        Assert.IsLessThanOrEqualTo(10.5f, text.SpawnTilePosition.X, "A number appears on the left half of its entity.");
        Assert.AreEqual(-1, text.VerticalDirection);
        Assert.AreEqual(0, text.AgeFrames);
    }

    [TestMethod]
    public void Update_SimulationPaused_NeitherReleasesNorAges()
    {
        var harness = new Harness();
        harness.Publish(3);

        harness.Controller.Update();
        Assert.AreEqual(0, harness.Controller.ActiveTextCount);

        harness.AdvanceFrames(1);
        harness.Controller.Update();
        harness.Controller.Update();

        Assert.AreEqual(0, harness.ActiveTexts().Single().AgeFrames);
    }

    [TestMethod]
    public void Update_SimultaneousEventsOnOneEntity_ReleasesOnePerInterval()
    {
        var harness = new Harness();
        harness.Publish(3, amount: 1);
        harness.Publish(3, amount: 2);
        harness.Publish(3, amount: 3);

        harness.AdvanceFrames(1);
        Assert.AreEqual(1, harness.Controller.ActiveTextCount);

        harness.AdvanceFrames(FloatingTextChrome.ReleaseIntervalFrames - 1);
        Assert.AreEqual(1, harness.Controller.ActiveTextCount);

        harness.AdvanceFrames(1);
        CollectionAssert.AreEqual(new ushort[] { 1, 2 }, harness.ActiveTexts().Select(static text => text.Amount).ToArray());
    }

    [TestMethod]
    public void Update_EventsOnDifferentEntities_ReleaseTogether()
    {
        var harness = new Harness();
        harness.Publish(3);
        harness.Publish(4);

        harness.AdvanceFrames(1);

        Assert.AreEqual(2, harness.Controller.ActiveTextCount);
    }

    [TestMethod]
    public void Update_EventAfterADrainedQueueWaitsOutTheInterval()
    {
        var harness = new Harness();
        harness.Publish(3);
        harness.AdvanceFrames(1);

        harness.Publish(3);
        harness.AdvanceFrames(1);

        Assert.AreEqual(1, harness.Controller.ActiveTextCount);

        harness.AdvanceFrames(FloatingTextChrome.ReleaseIntervalFrames);
        Assert.AreEqual(2, harness.Controller.ActiveTextCount);
    }

    [TestMethod]
    public void Publish_PastTheBacklog_AddsIntoTheNewestWaitingTextOfTheSameKind()
    {
        var harness = new Harness();
        var eventCount = FloatingTextChrome.MaxBacklogPerEntity + 3;
        for (var i = 0; i < eventCount; i++)
        {
            harness.Publish(3, amount: 1);
        }

        Assert.AreEqual(FloatingTextChrome.MaxBacklogPerEntity, harness.Controller.GetWaitingCount(3));

        harness.AdvanceFrames(FloatingTextChrome.MaxBacklogPerEntity * FloatingTextChrome.ReleaseIntervalFrames);
        Assert.AreEqual(eventCount, harness.ActiveTexts().Sum(static text => text.Amount));
    }

    [TestMethod]
    public void Publish_PastTheBacklog_KeepsDifferentKindsApart()
    {
        var harness = new Harness();
        for (var i = 0; i < FloatingTextChrome.MaxBacklogPerEntity; i++)
        {
            harness.Publish(3, FloatingTextKind.DamageTaken);
        }

        harness.Publish(3, FloatingTextKind.Healed);

        Assert.AreEqual(FloatingTextChrome.MaxBacklogPerEntity + 1, harness.Controller.GetWaitingCount(3));
    }

    [TestMethod]
    public void Update_AtTheEndOfItsLifetime_RemovesTheText()
    {
        var harness = new Harness();
        harness.Publish(3);
        harness.AdvanceFrames(1);

        harness.AdvanceFrames(FloatingTextChrome.LifetimeFrames - 1);
        Assert.AreEqual(1, harness.Controller.ActiveTextCount);

        harness.AdvanceFrames(1);
        Assert.AreEqual(0, harness.Controller.ActiveTextCount);
    }

    [TestMethod]
    public void Update_PastTheActiveCap_RetiresTheOldestText()
    {
        var harness = new Harness();
        harness.Publish(1, amount: 1);
        harness.AdvanceFrames(1);

        for (var entityId = 2; entityId <= FloatingTextChrome.MaxActiveTexts + 1; entityId++)
        {
            harness.Publish(entityId, amount: (ushort)entityId);
        }

        harness.AdvanceFrames(1);

        Assert.AreEqual(FloatingTextChrome.MaxActiveTexts, harness.Controller.ActiveTextCount);
        Assert.AreEqual(2, harness.Controller.GetActiveText(0).Amount);
        Assert.AreEqual(FloatingTextChrome.MaxActiveTexts + 1, harness.Controller.GetActiveText(FloatingTextChrome.MaxActiveTexts - 1).Amount);
    }

    [TestMethod]
    public void Update_NumberAndStatusOnOneEntity_ReleaseTogetherFromTheirOwnLanes()
    {
        var harness = new Harness();
        harness.Publish(3, FloatingTextKind.DamageTaken, x: 10);
        harness.Publish(3, FloatingTextKind.StatusEffectStacksAdded, x: 10, effectType: StatusEffectType.Burning);

        harness.AdvanceFrames(1);

        var texts = harness.ActiveTexts();
        Assert.HasCount(2, texts);
        Assert.IsLessThanOrEqualTo(10.5f, texts.Single(static text => text.Kind == FloatingTextKind.DamageTaken).SpawnTilePosition.X);
        Assert.IsGreaterThanOrEqualTo(10.5f, texts.Single(static text => text.Kind == FloatingTextKind.StatusEffectStacksAdded).SpawnTilePosition.X);
    }

    [TestMethod]
    [DataRow(FloatingTextKind.Dodged, 1)]
    [DataRow(FloatingTextKind.Immune, 1)]
    [DataRow(FloatingTextKind.DamageTaken, -1)]
    [DataRow(FloatingTextKind.StatusEffectStacksAdded, -1)]
    [DataRow(FloatingTextKind.Regenerated, -1)]
    public void Update_TextMovesInItsKindsDirection(FloatingTextKind kind, int expectedVerticalDirection)
    {
        var harness = new Harness();
        harness.Publish(3, kind);

        harness.AdvanceFrames(1);

        Assert.AreEqual(expectedVerticalDirection, harness.ActiveTexts().Single().VerticalDirection);
    }

    [TestMethod]
    [DataRow(FloatingTextKind.Dodged, 21f)]
    [DataRow(FloatingTextKind.Immune, 21f)]
    [DataRow(FloatingTextKind.DamageTaken, 20f)]
    public void Update_FallingTextStartsAtTheBottomOfTheFootprint_RisingTextAtTheTop(FloatingTextKind kind, float expectedSpawnY)
    {
        var harness = new Harness();
        harness.Publish(3, kind, y: 20);

        harness.AdvanceFrames(1);

        Assert.AreEqual(expectedSpawnY, harness.ActiveTexts().Single().SpawnTilePosition.Y);
    }

    [TestMethod]
    public void Publish_PastTheBacklog_KeepsDifferentEffectsApart()
    {
        var harness = new Harness();
        for (var i = 0; i < FloatingTextChrome.MaxBacklogPerEntity; i++)
        {
            harness.Publish(3, FloatingTextKind.StatusEffectStacksAdded, effectType: StatusEffectType.Burning);
        }

        harness.Publish(3, FloatingTextKind.StatusEffectStacksAdded, effectType: StatusEffectType.Poison);
        harness.Publish(3, FloatingTextKind.StatusEffectStacksAdded, effectType: StatusEffectType.Burning);

        Assert.AreEqual(FloatingTextChrome.MaxBacklogPerEntity + 1, harness.Controller.GetWaitingCount(3, FloatingTextLane.Statuses));
    }
}
