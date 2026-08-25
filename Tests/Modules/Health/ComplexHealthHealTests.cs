using Engine.ECS.Components.Stores;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.Health;

[TestClass]
public sealed class ComplexHealthHealTests
{
    private static MultiComponentPool<BodyPartComponent> CreateBodyPartsPool() =>
        new(maximumEntityCount: 10, initialCapacity: 8);

    private static Dictionary<string, BodyPartComponent> PartsByName(MultiComponentPool<BodyPartComponent> bodyParts, int entityId)
    {
        var result = new Dictionary<string, BodyPartComponent>();
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            var part = bodyParts.GetReadonlyByDenseIndex(denseIndex);
            result[part.Name] = part;
        }

        return result;
    }

    [TestMethod]
    public void ApplyFractionToAllParts_RaisesEachPartByFractionOfItsOwnMaximum()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Head", BodyPartType.Head, currentHealth: 10, maximumHealth: 30, isVital: true));
        bodyParts.Add(0, new BodyPartComponent("Leg", BodyPartType.Leg, currentHealth: 20, maximumHealth: 40, isVital: false));

        ComplexHealthHeal.ApplyFractionToAllParts(bodyParts, 0, 0.5f);

        var parts = PartsByName(bodyParts, 0);
        Assert.AreEqual(25f, parts["Head"].CurrentHealth);
        Assert.AreEqual(40f, parts["Leg"].CurrentHealth);
    }

    [TestMethod]
    public void ApplyFractionToAllParts_AlreadyFullPart_ClampsWithoutOverflow()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, currentHealth: 60, maximumHealth: 60, isVital: true));

        ComplexHealthHeal.ApplyFractionToAllParts(bodyParts, 0, 0.5f);

        var part = bodyParts.GetReadonlyByDenseIndex(bodyParts.GetFirstDenseIndex(0));
        Assert.AreEqual(60f, part.CurrentHealth);
    }

    [TestMethod]
    public void ApplyFractionToAllParts_LockedOutPart_HealsAnyway()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Arm", BodyPartType.Arm, currentHealth: 0, maximumHealth: 20, isVital: false));
        bodyParts.UpdateByDenseIndex(bodyParts.GetFirstDenseIndex(0), static (ref BodyPartComponent part) =>
        {
            part.IsDisabled = true;
            part.RegenLockoutFramesRemaining = 600;
        });

        ComplexHealthHeal.ApplyFractionToAllParts(bodyParts, 0, 0.5f);

        var part = bodyParts.GetReadonlyByDenseIndex(bodyParts.GetFirstDenseIndex(0));
        Assert.AreEqual(10f, part.CurrentHealth);
        Assert.AreEqual(600, part.RegenLockoutFramesRemaining, "The lockout is never consulted or reset by an active heal.");
    }

    [TestMethod]
    public void ApplyFractionToAllParts_PartHealedAboveZero_ClearsIsDisabled()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Arm", BodyPartType.Arm, currentHealth: 0, maximumHealth: 20, isVital: false));
        bodyParts.UpdateByDenseIndex(bodyParts.GetFirstDenseIndex(0), static (ref BodyPartComponent part) => part.IsDisabled = true);

        ComplexHealthHeal.ApplyFractionToAllParts(bodyParts, 0, 0.1f);

        var part = bodyParts.GetReadonlyByDenseIndex(bodyParts.GetFirstDenseIndex(0));
        Assert.IsFalse(part.IsDisabled);
    }

    [TestMethod]
    public void ApplyFractionToAllParts_MaximumHealthBuffActive_HealsPastRawMaximumToTheEffectiveOne()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Head", BodyPartType.Head, currentHealth: 40, maximumHealth: 40, isVital: true));
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: 0.5f, remainingDurationFrames: null, StatusEffectSource.Admin));

        // A part already at its raw maximum must still rise -- the true cap with a +50% buff
        // active is 60, not 40, matching ComplexHealthDamage/ComplexHealthRegenSystem's own
        // per-part effective-maximum clamp (see this method's own doc comment for the bug this
        // regression test covers: ApplyFractionToAllParts used to ignore statModifiers entirely).
        ComplexHealthHeal.ApplyFractionToAllParts(bodyParts, 0, 0.5f, statModifiers);

        var part = bodyParts.GetReadonlyByDenseIndex(bodyParts.GetFirstDenseIndex(0));
        Assert.AreEqual(60f, part.CurrentHealth, "40 + 60*0.5 = 70, clamped to the effective maximum of 60.");
    }

    [TestMethod]
    public void ApplyFractionToAllParts_MixedDamageEntity_EachPartRaisedByTheSameFraction_NotConverged()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Leg", BodyPartType.Leg, currentHealth: 8, maximumHealth: 40, isVital: false)); // 20%
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, currentHealth: 54, maximumHealth: 60, isVital: true)); // 90%

        ComplexHealthHeal.ApplyFractionToAllParts(bodyParts, 0, 0.25f);

        var parts = PartsByName(bodyParts, 0);
        // Leg: 8 + 40*0.25 = 18 (45%). Torso: 54 + 60*0.25 = 69, clamped to 60 (100%).
        Assert.AreEqual(18f, parts["Leg"].CurrentHealth);
        Assert.AreEqual(60f, parts["Torso"].CurrentHealth);
        Assert.AreNotEqual(parts["Leg"].CurrentHealth / parts["Leg"].MaximumHealth, parts["Torso"].CurrentHealth / parts["Torso"].MaximumHealth,
            "Each part rises by the same fraction of its own maximum, not toward a shared converged percentage.");
    }
}
