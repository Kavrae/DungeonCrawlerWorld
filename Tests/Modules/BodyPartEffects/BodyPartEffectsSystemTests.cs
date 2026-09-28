using Engine.ECS.Components.Stores;
using Game.Modules;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.BodyPartEffects.Systems;
using Game.Modules.Health;
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
    private static PackedComponentPool<MovementDisabledComponent> CreateMovementDisabledPool() =>
        new(entityCapacity: 10, initialCapacity: 10, static (ref existing, incoming) => { });

    private static PackedComponentPool<MeleeDisabledComponent> CreateMeleeDisabledPool() =>
        new(entityCapacity: 10, initialCapacity: 10, static (ref existing, incoming) => { });

    private static DirectComponentPool<ProcessingTierComponent> CreateTiersPool() =>
        new(initialCapacity: 10, static (ref existing, incoming) => existing = incoming);

    private static MultiComponentPool<StatModifierComponent> CreateStatModifiersPool() =>
        new(entityCapacity: 10, initialCapacity: 4);

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
        var world = BodyPartTestWorld.WithParts(0, ("Left Leg", BodyPartType.Leg, 50, 100, false));
        var statModifiers = CreateStatModifiersPool();
        var system = TestSystems.BodyPartEffectsSystem(world.BodyParts, world.States, CreateMovementDisabledPool(), CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);

        system.Update(default, 0);

        Assert.IsTrue(TryGetModifier(statModifiers, 0, StatModifierTarget.MovementLockFrames, out var magnitude));
        Assert.AreEqual(0.5f, magnitude, 0.001f, "50% HP -> 1.5x lock frames -> +0.5 multiplicative magnitude.");
        Assert.AreEqual(150f, StatModifierMath.GetEffectiveValue(statModifiers, 0, StatModifierTarget.MovementLockFrames, 100f), 0.01f);
    }

    [TestMethod]
    public void Update_TwoDamagedLegs_PenaltiesCompoundMultiplicatively()
    {
        var world = BodyPartTestWorld.WithParts(0, ("Left Leg", BodyPartType.Leg, 50, 100, false), ("Right Leg", BodyPartType.Leg, 50, 100, false));
        var statModifiers = CreateStatModifiersPool();
        var system = TestSystems.BodyPartEffectsSystem(world.BodyParts, world.States, CreateMovementDisabledPool(), CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);

        system.Update(default, 0);

        // Each leg alone is 1.5x -- two legs compound to 1.5*1.5 = 2.25x, not just 1.5x or a flat sum.
        Assert.AreEqual(225f, StatModifierMath.GetEffectiveValue(statModifiers, 0, StatModifierTarget.MovementLockFrames, 100f), 0.01f);
    }

    [TestMethod]
    public void Update_FullyHealedLeg_RemovesModifier()
    {
        var world = BodyPartTestWorld.WithParts(0, ("Left Leg", BodyPartType.Leg, 100, 100, false));
        var statModifiers = CreateStatModifiersPool();
        var system = TestSystems.BodyPartEffectsSystem(world.BodyParts, world.States, CreateMovementDisabledPool(), CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);

        system.Update(default, 0);

        Assert.IsFalse(TryGetModifier(statModifiers, 0, StatModifierTarget.MovementLockFrames, out _));
    }

    [TestMethod]
    public void Update_OneLegDisabledOneHealthy_GraduatedPenaltyNotHardBlock()
    {
        var world = BodyPartTestWorld.WithParts(0, ("Left Leg", BodyPartType.Leg, 0, 100, false), ("Right Leg", BodyPartType.Leg, 100, 100, false));
        var statModifiers = CreateStatModifiersPool();
        var movementDisabled = CreateMovementDisabledPool();
        var system = TestSystems.BodyPartEffectsSystem(world.BodyParts, world.States, movementDisabled, CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);

        system.Update(default, 0);

        Assert.IsFalse(movementDisabled.Has(0), "Only one of two legs is disabled -- movement should be penalized, not hard-blocked.");
        Assert.AreEqual(200f, StatModifierMath.GetEffectiveValue(statModifiers, 0, StatModifierTarget.MovementLockFrames, 100f), 0.01f, "Disabled leg contributes its full 2x; the healthy leg contributes 1x.");
    }

    [TestMethod]
    public void Update_EveryLegAndFootDisabled_HardBlocksMovementInsteadOfModifier()
    {
        var world = BodyPartTestWorld.WithParts(0, ("Left Leg", BodyPartType.Leg, 0, 100, false), ("Right Foot", BodyPartType.Foot, 0, 50, false));
        var statModifiers = CreateStatModifiersPool();
        var movementDisabled = CreateMovementDisabledPool();
        var system = TestSystems.BodyPartEffectsSystem(world.BodyParts, world.States, movementDisabled, CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);

        system.Update(default, 0);

        Assert.IsTrue(movementDisabled.Has(0));
        Assert.IsFalse(TryGetModifier(statModifiers, 0, StatModifierTarget.MovementLockFrames, out _), "Hard-blocked -- no lingering multiplier needed on top.");
    }

    [TestMethod]
    public void Update_FunctionalWing_SuppressesBothPenaltyAndHardBlockEvenWithBothLegsGone()
    {
        var world = BodyPartTestWorld.WithParts(0, ("Left Leg", BodyPartType.Leg, 0, 100, false), ("Right Leg", BodyPartType.Leg, 0, 100, false), ("Wing", BodyPartType.Wing, 20, 20, false));
        var statModifiers = CreateStatModifiersPool();
        var movementDisabled = CreateMovementDisabledPool();
        var system = TestSystems.BodyPartEffectsSystem(world.BodyParts, world.States, movementDisabled, CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);

        system.Update(default, 0);

        Assert.IsFalse(movementDisabled.Has(0));
        Assert.IsFalse(TryGetModifier(statModifiers, 0, StatModifierTarget.MovementLockFrames, out _));
    }

    [TestMethod]
    public void Update_DisabledWing_DoesNotSuppressLegPenalty()
    {
        var world = BodyPartTestWorld.WithParts(0, ("Left Leg", BodyPartType.Leg, 0, 100, false), ("Wing", BodyPartType.Wing, 0, 20, false));
        var statModifiers = CreateStatModifiersPool();
        var movementDisabled = CreateMovementDisabledPool();
        var system = TestSystems.BodyPartEffectsSystem(world.BodyParts, world.States, movementDisabled, CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);

        system.Update(default, 0);

        Assert.IsTrue(movementDisabled.Has(0), "The Wing is disabled too -- it can't fly, so the Leg hard block applies normally.");
    }

    [TestMethod]
    public void Update_OneDamagedArm_GrantsProportionalMeleeConditionalOutgoingDamageModifier()
    {
        var world = BodyPartTestWorld.WithParts(0, ("Left Arm", BodyPartType.Arm, 50, 100, false));
        var statModifiers = CreateStatModifiersPool();
        var system = TestSystems.BodyPartEffectsSystem(world.BodyParts, world.States, CreateMovementDisabledPool(), CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);

        system.Update(default, 0);

        Assert.AreEqual(50f, StatModifierMath.GetEffectiveValue(statModifiers, 0, StatModifierTarget.OutgoingDamage, 100f, [Tag.Melee]), 0.01f, "50% HP arm -> 0.5x melee damage.");
        Assert.AreEqual(100f, StatModifierMath.GetEffectiveValue(statModifiers, 0, StatModifierTarget.OutgoingDamage, 100f), 0.01f, "Untouched without Tag.Melee in the active tags -- this grant is melee-conditional.");
    }

    [TestMethod]
    public void Update_TwoDamagedArms_PenaltiesCompoundMultiplicatively()
    {
        var world = BodyPartTestWorld.WithParts(0, ("Left Arm", BodyPartType.Arm, 50, 100, false), ("Right Arm", BodyPartType.Arm, 50, 100, false));
        var statModifiers = CreateStatModifiersPool();
        var system = TestSystems.BodyPartEffectsSystem(world.BodyParts, world.States, CreateMovementDisabledPool(), CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);

        system.Update(default, 0);

        Assert.AreEqual(25f, StatModifierMath.GetEffectiveValue(statModifiers, 0, StatModifierTarget.OutgoingDamage, 100f, [Tag.Melee]), 0.01f, "0.5 * 0.5 = 0.25x, not 0.5x.");
    }

    [TestMethod]
    public void Update_EveryArmAndHandDisabled_HardBlocksMeleeInsteadOfModifier()
    {
        var world = BodyPartTestWorld.WithParts(0, ("Left Arm", BodyPartType.Arm, 0, 100, false), ("Right Hand", BodyPartType.Hand, 0, 30, false));
        var statModifiers = CreateStatModifiersPool();
        var meleeDisabled = CreateMeleeDisabledPool();
        var system = TestSystems.BodyPartEffectsSystem(world.BodyParts, world.States, CreateMovementDisabledPool(), meleeDisabled, CreateTiersPool(), new ProcessingTierEvents(), statModifiers);

        system.Update(default, 0);

        Assert.IsTrue(meleeDisabled.Has(0));
        Assert.IsFalse(TryGetModifier(statModifiers, 0, StatModifierTarget.OutgoingDamage, out _));
    }

    [TestMethod]
    public void Update_NoLegOrFootParts_NeverGrantsMovementModifierOrBlock()
    {
        var world = BodyPartTestWorld.WithParts(0, ("Torso", BodyPartType.Torso, 1, 100, true));
        var statModifiers = CreateStatModifiersPool();
        var movementDisabled = CreateMovementDisabledPool();
        var system = TestSystems.BodyPartEffectsSystem(world.BodyParts, world.States, movementDisabled, CreateMeleeDisabledPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers);

        system.Update(default, 0);

        Assert.IsFalse(movementDisabled.Has(0));
        Assert.IsFalse(TryGetModifier(statModifiers, 0, StatModifierTarget.MovementLockFrames, out _));
    }

}
