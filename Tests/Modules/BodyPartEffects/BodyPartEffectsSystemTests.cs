using Engine.ECS.Components.Stores;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.BodyPartEffects.Systems;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Tests.Modules.BodyPartEffects;

/// <summary>
/// Every test constructs BodyPartEffectsSystem against an empty bodyParts pool, then Adds body
/// parts afterward -- same "construct before Add" reasoning ComplexHealthRegenSystemTests/
/// BodyPartBurningSystemTests already document (ProcessingTierWiring.CreateAndWire seeds its
/// TieredEntityStripeSet from the driving pool's raw EntityIds span at construction time; adding
/// several parts to the same entity first would double/N-times count it).
/// </summary>
[TestClass]
public sealed class BodyPartEffectsSystemTests
{
    private static MultiComponentPool<BodyPartComponent> CreateBodyPartsPool() =>
        new(maximumEntityCount: 10, initialCapacity: 8);

    private static PackedComponentPool<MovementDisabledComponent> CreateMovementDisabledPool() =>
        new(maximumEntityCount: 10, initialCapacity: 10, static (ref existing, incoming) => { });

    private static PackedComponentPool<MeleeDisabledComponent> CreateMeleeDisabledPool() =>
        new(maximumEntityCount: 10, initialCapacity: 10, static (ref existing, incoming) => { });

    private static DirectComponentPool<ProcessingTierComponent> CreateTiersPool() =>
        new(initialCapacity: 10, static (ref existing, incoming) => existing = incoming);

    private static MultiComponentPool<StatModifierComponent> CreateStatModifiersPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4);

    private static bool TryGetModifier(MultiComponentPool<StatModifierComponent> statModifiers, int entityId, StatModifierTarget target, out float magnitude)
    {
        for (var denseIndex = statModifiers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = statModifiers.GetNextDenseIndex(denseIndex))
        {
            var modifier = statModifiers.GetReadonlyByDenseIndex(denseIndex);
            if (modifier.Target == target)
            {
                magnitude = modifier.Magnitude;
                return true;
            }
        }

        magnitude = 0f;
        return false;
    }

    [TestMethod]
    public void Update_OneDamagedLeg_GrantsProportionalMovementLockFramesModifier()
    {
        var bodyParts = CreateBodyPartsPool();
        var statModifiers = CreateStatModifiersPool();
        var system = new BodyPartEffectsSystem(bodyParts, CreateMovementDisabledPool(), CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);
        bodyParts.Add(0, new BodyPartComponent("Left Leg", BodyPartType.Leg, partId: 0, verticalPosition: 1, currentHealth: 50, maximumHealth: 100, isVital: false));

        system.Update(default, 0);

        Assert.IsTrue(TryGetModifier(statModifiers, 0, StatModifierTarget.MovementLockFrames, out var magnitude));
        Assert.AreEqual(0.5f, magnitude, 0.001f, "50% HP -> 1.5x lock frames -> +0.5 multiplicative magnitude.");
        Assert.AreEqual(150f, StatModifierMath.GetEffectiveValue(statModifiers, 0, StatModifierTarget.MovementLockFrames, 100f), 0.01f);
    }

    [TestMethod]
    public void Update_TwoDamagedLegs_PenaltiesCompoundMultiplicatively()
    {
        var bodyParts = CreateBodyPartsPool();
        var statModifiers = CreateStatModifiersPool();
        var system = new BodyPartEffectsSystem(bodyParts, CreateMovementDisabledPool(), CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);
        bodyParts.Add(0, new BodyPartComponent("Left Leg", BodyPartType.Leg, partId: 0, verticalPosition: 1, currentHealth: 50, maximumHealth: 100, isVital: false));
        bodyParts.Add(0, new BodyPartComponent("Right Leg", BodyPartType.Leg, partId: 1, verticalPosition: 1, currentHealth: 50, maximumHealth: 100, isVital: false));

        system.Update(default, 0);

        // Each leg alone is 1.5x -- two legs compound to 1.5*1.5 = 2.25x, not just 1.5x or a flat sum.
        Assert.AreEqual(225f, StatModifierMath.GetEffectiveValue(statModifiers, 0, StatModifierTarget.MovementLockFrames, 100f), 0.01f);
    }

    [TestMethod]
    public void Update_FullyHealedLeg_RemovesModifier()
    {
        var bodyParts = CreateBodyPartsPool();
        var statModifiers = CreateStatModifiersPool();
        var system = new BodyPartEffectsSystem(bodyParts, CreateMovementDisabledPool(), CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);
        bodyParts.Add(0, new BodyPartComponent("Left Leg", BodyPartType.Leg, partId: 0, verticalPosition: 1, currentHealth: 100, maximumHealth: 100, isVital: false));

        system.Update(default, 0);

        Assert.IsFalse(TryGetModifier(statModifiers, 0, StatModifierTarget.MovementLockFrames, out _));
    }

    [TestMethod]
    public void Update_OneLegDisabledOneHealthy_GraduatedPenaltyNotHardBlock()
    {
        var bodyParts = CreateBodyPartsPool();
        var statModifiers = CreateStatModifiersPool();
        var movementDisabled = CreateMovementDisabledPool();
        var system = new BodyPartEffectsSystem(bodyParts, movementDisabled, CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);
        bodyParts.Add(0, new BodyPartComponent("Left Leg", BodyPartType.Leg, partId: 0, verticalPosition: 1, currentHealth: 0, maximumHealth: 100, isVital: false) { IsDisabled = true });
        bodyParts.Add(0, new BodyPartComponent("Right Leg", BodyPartType.Leg, partId: 1, verticalPosition: 1, currentHealth: 100, maximumHealth: 100, isVital: false));

        system.Update(default, 0);

        Assert.IsFalse(movementDisabled.Has(0), "Only one of two legs is disabled -- movement should be penalized, not hard-blocked.");
        Assert.AreEqual(200f, StatModifierMath.GetEffectiveValue(statModifiers, 0, StatModifierTarget.MovementLockFrames, 100f), 0.01f, "Disabled leg contributes its full 2x; the healthy leg contributes 1x.");
    }

    [TestMethod]
    public void Update_EveryLegAndFootDisabled_HardBlocksMovementInsteadOfModifier()
    {
        var bodyParts = CreateBodyPartsPool();
        var statModifiers = CreateStatModifiersPool();
        var movementDisabled = CreateMovementDisabledPool();
        var system = new BodyPartEffectsSystem(bodyParts, movementDisabled, CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);
        bodyParts.Add(0, new BodyPartComponent("Left Leg", BodyPartType.Leg, partId: 0, verticalPosition: 1, currentHealth: 0, maximumHealth: 100, isVital: false) { IsDisabled = true });
        bodyParts.Add(0, new BodyPartComponent("Right Foot", BodyPartType.Foot, partId: 1, verticalPosition: 0, currentHealth: 0, maximumHealth: 50, isVital: false) { IsDisabled = true });

        system.Update(default, 0);

        Assert.IsTrue(movementDisabled.Has(0));
        Assert.IsFalse(TryGetModifier(statModifiers, 0, StatModifierTarget.MovementLockFrames, out _), "Hard-blocked -- no lingering multiplier needed on top.");
    }

    [TestMethod]
    public void Update_FunctionalWing_SuppressesBothPenaltyAndHardBlockEvenWithBothLegsGone()
    {
        var bodyParts = CreateBodyPartsPool();
        var statModifiers = CreateStatModifiersPool();
        var movementDisabled = CreateMovementDisabledPool();
        var system = new BodyPartEffectsSystem(bodyParts, movementDisabled, CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);
        bodyParts.Add(0, new BodyPartComponent("Left Leg", BodyPartType.Leg, partId: 0, verticalPosition: 1, currentHealth: 0, maximumHealth: 100, isVital: false) { IsDisabled = true });
        bodyParts.Add(0, new BodyPartComponent("Right Leg", BodyPartType.Leg, partId: 1, verticalPosition: 1, currentHealth: 0, maximumHealth: 100, isVital: false) { IsDisabled = true });
        bodyParts.Add(0, new BodyPartComponent("Wing", BodyPartType.Wing, partId: 2, verticalPosition: 5, currentHealth: 20, maximumHealth: 20, isVital: false));

        system.Update(default, 0);

        Assert.IsFalse(movementDisabled.Has(0));
        Assert.IsFalse(TryGetModifier(statModifiers, 0, StatModifierTarget.MovementLockFrames, out _));
    }

    [TestMethod]
    public void Update_DisabledWing_DoesNotSuppressLegPenalty()
    {
        var bodyParts = CreateBodyPartsPool();
        var statModifiers = CreateStatModifiersPool();
        var movementDisabled = CreateMovementDisabledPool();
        var system = new BodyPartEffectsSystem(bodyParts, movementDisabled, CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);
        bodyParts.Add(0, new BodyPartComponent("Left Leg", BodyPartType.Leg, partId: 0, verticalPosition: 1, currentHealth: 0, maximumHealth: 100, isVital: false) { IsDisabled = true });
        bodyParts.Add(0, new BodyPartComponent("Wing", BodyPartType.Wing, partId: 1, verticalPosition: 5, currentHealth: 0, maximumHealth: 20, isVital: false) { IsDisabled = true });

        system.Update(default, 0);

        Assert.IsTrue(movementDisabled.Has(0), "The Wing is disabled too -- it can't fly, so the Leg hard block applies normally.");
    }

    [TestMethod]
    public void Update_OneDamagedArm_GrantsProportionalMeleeOutgoingDamageModifier()
    {
        var bodyParts = CreateBodyPartsPool();
        var statModifiers = CreateStatModifiersPool();
        var system = new BodyPartEffectsSystem(bodyParts, CreateMovementDisabledPool(), CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);
        bodyParts.Add(0, new BodyPartComponent("Left Arm", BodyPartType.Arm, partId: 0, verticalPosition: 3, currentHealth: 50, maximumHealth: 100, isVital: false));

        system.Update(default, 0);

        Assert.AreEqual(50f, StatModifierMath.GetEffectiveValue(statModifiers, 0, StatModifierTarget.MeleeOutgoingDamage, 100f), 0.01f, "50% HP arm -> 0.5x melee damage.");
    }

    [TestMethod]
    public void Update_TwoDamagedArms_PenaltiesCompoundMultiplicatively()
    {
        var bodyParts = CreateBodyPartsPool();
        var statModifiers = CreateStatModifiersPool();
        var system = new BodyPartEffectsSystem(bodyParts, CreateMovementDisabledPool(), CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);
        bodyParts.Add(0, new BodyPartComponent("Left Arm", BodyPartType.Arm, partId: 0, verticalPosition: 3, currentHealth: 50, maximumHealth: 100, isVital: false));
        bodyParts.Add(0, new BodyPartComponent("Right Arm", BodyPartType.Arm, partId: 1, verticalPosition: 3, currentHealth: 50, maximumHealth: 100, isVital: false));

        system.Update(default, 0);

        Assert.AreEqual(25f, StatModifierMath.GetEffectiveValue(statModifiers, 0, StatModifierTarget.MeleeOutgoingDamage, 100f), 0.01f, "0.5 * 0.5 = 0.25x, not 0.5x.");
    }

    [TestMethod]
    public void Update_EveryArmAndHandDisabled_HardBlocksMeleeInsteadOfModifier()
    {
        var bodyParts = CreateBodyPartsPool();
        var statModifiers = CreateStatModifiersPool();
        var meleeDisabled = CreateMeleeDisabledPool();
        var system = new BodyPartEffectsSystem(bodyParts, CreateMovementDisabledPool(), meleeDisabled, CreateTiersPool(), new ProcessingTierEvents(), statModifiers);
        bodyParts.Add(0, new BodyPartComponent("Left Arm", BodyPartType.Arm, partId: 0, verticalPosition: 3, currentHealth: 0, maximumHealth: 100, isVital: false) { IsDisabled = true });
        bodyParts.Add(0, new BodyPartComponent("Right Hand", BodyPartType.Hand, partId: 1, verticalPosition: 2, currentHealth: 0, maximumHealth: 30, isVital: false) { IsDisabled = true });

        system.Update(default, 0);

        Assert.IsTrue(meleeDisabled.Has(0));
        Assert.IsFalse(TryGetModifier(statModifiers, 0, StatModifierTarget.MeleeOutgoingDamage, out _));
    }

    [TestMethod]
    public void Update_NoLegOrFootParts_NeverGrantsMovementModifierOrBlock()
    {
        var bodyParts = CreateBodyPartsPool();
        var statModifiers = CreateStatModifiersPool();
        var movementDisabled = CreateMovementDisabledPool();
        var system = new BodyPartEffectsSystem(bodyParts, movementDisabled, CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, partId: 0, verticalPosition: 4, currentHealth: 1, maximumHealth: 100, isVital: true));

        system.Update(default, 0);

        Assert.IsFalse(movementDisabled.Has(0));
        Assert.IsFalse(TryGetModifier(statModifiers, 0, StatModifierTarget.MovementLockFrames, out _));
    }

    [TestMethod]
    public void Update_NoStatModifierPoolRegistered_DoesNotThrow()
    {
        var bodyParts = CreateBodyPartsPool();
        var system = new BodyPartEffectsSystem(bodyParts, CreateMovementDisabledPool(), CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents());
        bodyParts.Add(0, new BodyPartComponent("Left Leg", BodyPartType.Leg, partId: 0, verticalPosition: 1, currentHealth: 50, maximumHealth: 100, isVital: false));

        system.Update(default, 0);
    }
}
