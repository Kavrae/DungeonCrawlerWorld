using Engine.ECS.Components.Stores;
using Engine.Utilities;
using Game.Modules.Health;
using Game.Modules.Health.Components;

namespace Tests.Modules.Health;

[TestClass]
public sealed class BodyPartDamageEffectsTests
{
    private static MultiComponentPool<BodyPartComponent> CreateBodyPartsPool() =>
        new(maximumEntityCount: 10, initialCapacity: 8);

    [TestMethod]
    public void ApplyToPart_HitLandsAtZero_DisablesAndSetsFreshLockout()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Arm", BodyPartType.Arm, partId: 0, verticalPosition: 0, currentHealth: 5, maximumHealth: 20, isVital: false));
        var denseIndex = bodyParts.GetFirstDenseIndex(0);

        BodyPartDamageEffects.ApplyToPart(bodyParts, denseIndex, statModifiers: null, entityId: 0, amount: 10);

        var part = bodyParts.GetReadonlyByDenseIndex(denseIndex);
        Assert.AreEqual(0f, part.CurrentHealth);
        Assert.IsTrue(part.IsDisabled);
        Assert.AreEqual(10 * GameTiming.FramesPerSecond, part.RegenLockoutFramesRemaining);
    }

    /// <summary>The lockout re-arms on every hit that leaves a part at 0, not only the first transition into 0 -- a second hit against an already-disabled part (e.g. a burning part's own repeat DoT tick) must not let the lockout quietly keep counting down from the first hit.</summary>
    [TestMethod]
    public void ApplyToPart_SecondHitAgainstAlreadyZeroPart_RearmsLockoutFromFresh()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Arm", BodyPartType.Arm, partId: 0, verticalPosition: 0, currentHealth: 5, maximumHealth: 20, isVital: false));
        var denseIndex = bodyParts.GetFirstDenseIndex(0);

        BodyPartDamageEffects.ApplyToPart(bodyParts, denseIndex, statModifiers: null, entityId: 0, amount: 10);

        // Simulate time passing -- the lockout counts partway down before the second hit lands.
        bodyParts.UpdateByDenseIndex(denseIndex, static (ref BodyPartComponent part) => part.RegenLockoutFramesRemaining = 5);

        BodyPartDamageEffects.ApplyToPart(bodyParts, denseIndex, statModifiers: null, entityId: 0, amount: 1);

        var part = bodyParts.GetReadonlyByDenseIndex(denseIndex);
        Assert.AreEqual(0f, part.CurrentHealth);
        Assert.IsTrue(part.IsDisabled);
        Assert.AreEqual(10 * GameTiming.FramesPerSecond, part.RegenLockoutFramesRemaining, "The second 0-landing hit must reset the lockout to a fresh 10 seconds, not leave the partway-decayed value from the first hit.");
    }

    [TestMethod]
    public void ApplyToPart_HitDoesNotReachZero_DoesNotDisableOrSetLockout()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, partId: 0, verticalPosition: 0, currentHealth: 60, maximumHealth: 60, isVital: true));
        var denseIndex = bodyParts.GetFirstDenseIndex(0);

        BodyPartDamageEffects.ApplyToPart(bodyParts, denseIndex, statModifiers: null, entityId: 0, amount: 10);

        var part = bodyParts.GetReadonlyByDenseIndex(denseIndex);
        Assert.AreEqual(50f, part.CurrentHealth);
        Assert.IsFalse(part.IsDisabled);
        Assert.AreEqual((ushort)0, part.RegenLockoutFramesRemaining);
    }

    [TestMethod]
    public void ApplyToPart_ClampsAtZero_DoesNotGoNegative()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Arm", BodyPartType.Arm, partId: 0, verticalPosition: 0, currentHealth: 3, maximumHealth: 20, isVital: false));
        var denseIndex = bodyParts.GetFirstDenseIndex(0);

        BodyPartDamageEffects.ApplyToPart(bodyParts, denseIndex, statModifiers: null, entityId: 0, amount: 100);

        Assert.AreEqual(0f, bodyParts.GetReadonlyByDenseIndex(denseIndex).CurrentHealth);
    }
}
