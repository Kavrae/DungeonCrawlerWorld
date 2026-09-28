using Engine.ECS.Systems;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Presentation.UI.Content;

namespace Tests.Presentation;

[TestClass]
public sealed class InspectionWindowContentTests
{
    private const int EntityId = 0;

    private static PackedComponentPool<SimpleHealthComponent> CreateHealthPool() =>
        new(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static MultiComponentPool<StatModifierComponent> CreateMaximumHealthBuffPool(float magnitude)
    {
        var statModifiers = new MultiComponentPool<StatModifierComponent>(entityCapacity: 10, initialCapacity: 4);
        statModifiers.Add(EntityId, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: magnitude, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));
        return statModifiers;
    }

    [TestMethod]
    public void ReplaceHealthEntriesWithEffectiveMaximum_SimpleHealthWithBuff_ShowsEffectiveMaximumNotRaw()
    {
        var healthPool = CreateHealthPool();
        healthPool.Add(EntityId, new SimpleHealthComponent(currentHealth: 120, maximumHealth: 100));
        var bodyParts = new BodyPartTestWorld().BodyParts;
        var statModifiers = CreateMaximumHealthBuffPool(0.5f);
        List<InspectedComponentEntry> entries = [new InspectedComponentEntry(typeof(SimpleHealthComponent), "stale raw-formatted text", 0)];

        InspectionWindowContent.ReplaceHealthEntriesWithEffectiveMaximum(entries, EntityId, healthPool, bodyParts, statModifiers);

        Assert.HasCount(1, entries);
        Assert.AreEqual(typeof(SimpleHealthComponent), entries[0].ComponentType);
        Assert.Contains("120/150", entries[0].Value, "Effective maximum is 100*1.5=150, not the raw 100.");
    }

    [TestMethod]
    public void ReplaceHealthEntriesWithEffectiveMaximum_BodyPartsWithBuff_EachShowsItsOwnEffectiveMaximum()
    {
        var healthPool = CreateHealthPool();
        // Head sits above its raw maximum, which only a MaximumHealth buff makes possible.
        var partsWorld = BodyPartTestWorld.WithParts(EntityId, ("Head", BodyPartType.Head, 40, 40, true), ("Torso", BodyPartType.Torso, 60, 80, true));
        partsWorld.BodyParts.SetCurrentHealth(EntityId, 0, 50f);
        var bodyParts = partsWorld.BodyParts;
        var statModifiers = CreateMaximumHealthBuffPool(0.5f);
        List<InspectedComponentEntry> entries = [];

        InspectionWindowContent.ReplaceHealthEntriesWithEffectiveMaximum(entries, EntityId, healthPool, bodyParts, statModifiers);

        Assert.HasCount(2, entries);
        Assert.IsTrue(entries.TrueForAll(entry => entry.ComponentType == typeof(BodyPartStateComponent)));
        Assert.IsTrue(entries.Exists(entry => entry.Value.Contains("50/60")), "Head: raw 40 * 1.5 = 60.");
        Assert.IsTrue(entries.Exists(entry => entry.Value.Contains("60/120")), "Torso: raw 80 * 1.5 = 120.");
    }

    [TestMethod]
    public void ReplaceHealthEntriesWithEffectiveMaximum_NoActiveModifiers_MatchesRawMaximum()
    {
        var healthPool = CreateHealthPool();
        healthPool.Add(EntityId, new SimpleHealthComponent(currentHealth: 80, maximumHealth: 100));
        var bodyParts = new BodyPartTestWorld().BodyParts;
        List<InspectedComponentEntry> entries = [];

        InspectionWindowContent.ReplaceHealthEntriesWithEffectiveMaximum(entries, EntityId, healthPool, bodyParts, statModifiers: EmptyPools.Multi<StatModifierComponent>());

        Assert.HasCount(1, entries);
        Assert.Contains("80/100", entries[0].Value);
    }

    [TestMethod]
    public void ReplaceHealthEntriesWithEffectiveMaximum_RemovesGenericEntriesEvenWithNoReplacement()
    {
        var healthPool = CreateHealthPool();
        var bodyParts = new BodyPartTestWorld().BodyParts;
        List<InspectedComponentEntry> entries =
        [
            new InspectedComponentEntry(typeof(SimpleHealthComponent), "stale", 0),
            new InspectedComponentEntry(typeof(BodyPartStateComponent), "stale", 0),
            new InspectedComponentEntry(typeof(RaceComponentPlaceholder), "unrelated, left alone", 0),
        ];

        InspectionWindowContent.ReplaceHealthEntriesWithEffectiveMaximum(entries, EntityId, healthPool, bodyParts, statModifiers: EmptyPools.Multi<StatModifierComponent>());

        Assert.HasCount(1, entries);
        Assert.AreEqual(typeof(RaceComponentPlaceholder), entries[0].ComponentType);
    }

    private sealed class RaceComponentPlaceholder;
}
