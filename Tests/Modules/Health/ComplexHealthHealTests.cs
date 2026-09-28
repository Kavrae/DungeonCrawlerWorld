using Engine.Events;
using Engine.ECS.Systems;
using Engine.ECS.Components.Stores;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Game.Modules.Death.Components;
using Game.Modules.AbilityScores.Components;

namespace Tests.Modules.Health;

/// <summary>
/// ApplyToAllParts computes its total once against the entity's overall effective max health,
/// then splits it evenly across however many parts exist -- not the old per-part "same fraction
/// of its own max" scaling (see ComplexHealthHeal's own doc comment for why: an equal fraction
/// approach lets a flat/additive component be multiplied by part count). Single-part fixtures
/// (partCount 1) numerically match the old per-part behavior exactly, since splitting a total
/// across one part is a no-op -- only the multi-part fixtures actually differ.
/// </summary>
[TestClass]
public sealed class ComplexHealthHealTests
{
    private static PackedComponentPool<SimpleHealthComponent> CreateHealthPool() =>
        new(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static Dictionary<string, BodyPartView> PartsByName(EntityBodyParts bodyParts, int entityId)
    {
        var result = new Dictionary<string, BodyPartView>();
        foreach (var part in bodyParts.Parts(entityId))
        {
            result[part.Name] = part;
        }

        return result;
    }

    [TestMethod]
    public void ApplyToAllParts_SplitsTotalEvenlyAcrossParts_NotByEachPartsOwnMaximum()
    {
        var bodyParts = BodyPartTestWorld.WithParts(0, ("Head", BodyPartType.Head, 10, 30, true), ("Leg", BodyPartType.Leg, 20, 40, false)).BodyParts;

        TestHealth.HealAllParts(bodyParts, CreateHealthPool(), 0, percentOfMaxHealth: 0.5f);

        // Total = 50% of the entity's overall max (30+40=70) = 35, split evenly across 2 parts = 17.5 each.
        var parts = PartsByName(bodyParts, 0);
        Assert.AreEqual(27.5f, parts["Head"].CurrentHealth, "10 + 17.5 -- not 10 + 0.5*30=25, the old per-part-fraction result.");
        Assert.AreEqual(37.5f, parts["Leg"].CurrentHealth, "20 + 17.5 -- not 20 + 0.5*40=40, the old per-part-fraction result.");
    }

    [TestMethod]
    public void ApplyToAllParts_SinglePart_AlreadyFullPart_ClampsWithoutOverflow()
    {
        var bodyParts = BodyPartTestWorld.WithParts(0, ("Torso", BodyPartType.Torso, 60, 60, true)).BodyParts;

        TestHealth.HealAllParts(bodyParts, CreateHealthPool(), 0, percentOfMaxHealth: 0.5f);

        bodyParts.TryGet(0, 0, out var part);
        Assert.AreEqual(60f, part.CurrentHealth);
    }

    [TestMethod]
    public void ApplyToAllParts_SinglePart_LockedOutPart_HealsAnyway()
    {
        var bodyParts = BodyPartTestWorld.WithParts(0, ("Arm", BodyPartType.Arm, 0, 20, false)).BodyParts;
        // Locked out of passive regen, which an active heal ignores.
        bodyParts.LockOutOfRegen(0, 0, now: 0, lockoutFrames: 600);

        TestHealth.HealAllParts(bodyParts, CreateHealthPool(), 0, percentOfMaxHealth: 0.5f);

        bodyParts.TryGet(0, 0, out var part);
        Assert.AreEqual(10f, part.CurrentHealth);
        Assert.AreEqual(600u, part.RegenLockedUntilFrame, "The lockout is never consulted or reset by an active heal.");
    }

    [TestMethod]
    public void ApplyToAllParts_SinglePart_PartHealedAboveZero_ClearsIsDisabled()
    {
        var bodyParts = BodyPartTestWorld.WithParts(0, ("Arm", BodyPartType.Arm, 0, 20, false)).BodyParts;

        TestHealth.HealAllParts(bodyParts, CreateHealthPool(), 0, percentOfMaxHealth: 0.1f);

        bodyParts.TryGet(0, 0, out var part);
        Assert.IsFalse(part.IsDisabled);
    }

    [TestMethod]
    public void ApplyToAllParts_SinglePart_MaximumHealthBuffActive_HealsPastRawMaximumToTheEffectiveOne()
    {
        var bodyParts = BodyPartTestWorld.WithParts(0, ("Head", BodyPartType.Head, 40, 40, true)).BodyParts;
        var statModifiers = new MultiComponentPool<StatModifierComponent>(entityCapacity: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: 0.5f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));

        // A part already at its raw maximum must still rise -- the true cap with a +50% buff
        // active is 60, not 40 (both for the total's own percent-of-max calculation and for the
        // per-part clamp), matching ComplexHealthDamage/ComplexHealthRegenSystem's own
        // effective-maximum clamp.
        TestHealth.HealAllParts(bodyParts, CreateHealthPool(), 0, percentOfMaxHealth: 0.5f, statModifiers: statModifiers);

        bodyParts.TryGet(0, 0, out var part);
        Assert.AreEqual(60f, part.CurrentHealth, "50% of the effective max (60) = 30 total; 40 + 30 = 70, clamped to 60.");
    }

    [TestMethod]
    public void ApplyToAllParts_MixedDamageEntity_EachPartReceivesAnEqualAbsoluteShare_NotTheSameFraction()
    {
        var bodyParts = BodyPartTestWorld.WithParts(0, ("Leg", BodyPartType.Leg, 8, 40, false), ("Torso", BodyPartType.Torso, 54, 60, true)).BodyParts;

        TestHealth.HealAllParts(bodyParts, CreateHealthPool(), 0, percentOfMaxHealth: 0.25f);

        // Total = 25% of the entity's overall max (40+60=100) = 25, split evenly = 12.5 each --
        // both parts gain the same absolute 12.5 (Torso clamped), not the same 25% fraction of
        // their own maximum (which would have been Leg +10, Torso +15).
        var parts = PartsByName(bodyParts, 0);
        Assert.AreEqual(20.5f, parts["Leg"].CurrentHealth, "8 + 12.5 = 20.5.");
        Assert.AreEqual(60f, parts["Torso"].CurrentHealth, "54 + 12.5 = 66.5, clamped to 60.");
    }

    [TestMethod]
    public void ApplyToAllParts_FlatAmount_NotMultipliedByPartCount()
    {
        var bodyParts = BodyPartTestWorld.WithParts(0, ("Head", BodyPartType.Head, 0, 100, true), ("Torso", BodyPartType.Torso, 0, 100, true), ("Leg", BodyPartType.Leg, 0, 100, false)).BodyParts;

        TestHealth.HealAllParts(bodyParts, CreateHealthPool(), 0, percentOfMaxHealth: 0f, flatAmount: 30f);

        // A flat 30 heal must total 30 across the whole entity (10 per part across 3 parts), not
        // 30 landing on every one of the 3 parts (which would total 90).
        var parts = PartsByName(bodyParts, 0);
        Assert.AreEqual(10f, parts["Head"].CurrentHealth);
        Assert.AreEqual(10f, parts["Torso"].CurrentHealth);
        Assert.AreEqual(10f, parts["Leg"].CurrentHealth);
    }

    [TestMethod]
    public void ApplyToAllParts_LowestPercentageMode_HealsOnlyTheMostDamagedPart()
    {
        var bodyParts = BodyPartTestWorld.WithParts(0, ("Head", BodyPartType.Head, 90, 100, true), ("Leg", BodyPartType.Leg, 20, 100, false)).BodyParts;

        ComplexHealthHeal.ApplyToSinglePart(bodyParts, CreateHealthPool(), 0, percentOfMaxHealth: 0.1f, flatAmount: 0f, statModifiers: EmptyPools.Multi<StatModifierComponent>(), sourceEntityId: null, activatorTags: null, targetRule: null, targetMode: BodyPartTargetMode.LowestPercentage, mathUtility: null, now: 0, eventBus: new EventBus(), playerQuery: TestPlayerQuery.NoPlayer);

        // Total = 10% of the overall max (100+100=200) = 20, applied entirely to the Leg (lowest percentage).
        var parts = PartsByName(bodyParts, 0);
        Assert.AreEqual(90f, parts["Head"].CurrentHealth, "Untouched -- only the most-damaged part is healed.");
        Assert.AreEqual(40f, parts["Leg"].CurrentHealth, "20 + 20 = 40.");
    }
}
