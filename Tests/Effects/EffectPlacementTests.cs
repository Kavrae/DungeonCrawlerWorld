using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.Events;
using Engine.Math;
using Game.Effects;

namespace Tests.Effects;

/// <summary>Entries placed on each target apply per target; entries placed once apply once, on the marked entity or at the centre tile.</summary>
[TestClass]
public sealed class EffectPlacementTests
{
    private static readonly Vector3Int Centre = new(4, 4, 0);

    private sealed record RecordingEntry(EffectPlacement Placement, List<(int Target, Vector3Int? Location)> Log) : IEffectEntry
    {
        EffectPlacement IEffectEntry.Placement => Placement;

        public EffectOutcome Apply(in EffectContext context)
        {
            Log.Add((context.TargetEntityId, context.TargetLocation));
            return EffectOutcome.Applied;
        }
    }

    private static EffectContext Context() =>
        new(TestActionEffects.Services(BuiltInTestComponents.RegisterAll(new ComponentManager(16, 16)), new EntityKeys(), new EventBus(), new MathUtility(new Random(1))),
            Game.World.ActionSource.Admin, SourceEntityId: 1, TargetEntityId: 1, "Test", default, Now: 0);

    [TestMethod]
    public void ApplyOnEachTarget_AppliesOnlyEntriesPlacedOnEachTarget()
    {
        var each = new List<(int, Vector3Int?)>();
        var once = new List<(int, Vector3Int?)>();
        IReadOnlyList<Effect> effects = [new Effect([new RecordingEntry(EffectPlacement.OnEachTarget, each), new RecordingEntry(EffectPlacement.OncePerActivation, once)])];

        foreach (var target in new[] { 7, 8, 9 })
        {
            EffectSequence.ApplyOnEachTarget(effects, Context() with { TargetEntityId = target });
        }

        CollectionAssert.AreEqual(new[] { 7, 8, 9 }, each.Select(application => application.Item1).ToArray());
        Assert.IsEmpty(once);
    }

    [TestMethod]
    public void ApplyOnce_OncePerActivation_LandsOnTheMarkedEntity()
    {
        var once = new List<(int, Vector3Int?)>();
        IReadOnlyList<Effect> effects = [new Effect([new RecordingEntry(EffectPlacement.OncePerActivation, once)])];

        EffectSequence.ApplyOnce(effects, Context(), markedEntityId: 8, Centre);

        CollectionAssert.AreEqual(new (int, Vector3Int?)[] { (8, Centre) }, once);
    }

    [TestMethod]
    public void ApplyOnce_OncePerActivationWithNothingMarked_LandsAtTheCentreWithNoTargetEntity()
    {
        var once = new List<(int, Vector3Int?)>();
        IReadOnlyList<Effect> effects = [new Effect([new RecordingEntry(EffectPlacement.OncePerActivation, once)])];

        EffectSequence.ApplyOnce(effects, Context(), markedEntityId: null, Centre);

        CollectionAssert.AreEqual(new (int, Vector3Int?)[] { (EffectContext.NoTargetEntity, Centre) }, once);
    }

    [TestMethod]
    public void ApplyOnce_AtLocation_LandsAtTheCentreEvenWithAnEntityMarked()
    {
        var atLocation = new List<(int, Vector3Int?)>();
        var each = new List<(int, Vector3Int?)>();
        IReadOnlyList<Effect> effects = [new Effect([new RecordingEntry(EffectPlacement.AtLocation, atLocation), new RecordingEntry(EffectPlacement.OnEachTarget, each)])];

        EffectSequence.ApplyOnce(effects, Context(), markedEntityId: 8, Centre);

        CollectionAssert.AreEqual(new (int, Vector3Int?)[] { (EffectContext.NoTargetEntity, Centre) }, atLocation);
        Assert.IsEmpty(each, "ApplyOnce leaves the per-target entries to the per-target pass.");
    }

    [TestMethod]
    public void Apply_ToASingleEntity_AppliesEveryEntryThere_WhateverItsPlacement()
    {
        var each = new List<(int, Vector3Int?)>();
        var once = new List<(int, Vector3Int?)>();
        IReadOnlyList<Effect> effects = [new Effect([new RecordingEntry(EffectPlacement.OnEachTarget, each), new RecordingEntry(EffectPlacement.OncePerActivation, once)])];

        EffectSequence.Apply(effects, Context() with { TargetEntityId = 8 });

        Assert.HasCount(1, each);
        CollectionAssert.AreEqual(new (int, Vector3Int?)[] { (8, null) }, once, "An aura's tick or a contact applies to the one entity, with no location.");
    }
}
