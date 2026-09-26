using Engine.Utilities;
using Game.Modules.Health;
using Game.Modules.Health.Components;

namespace Tests.Modules.Health;

[TestClass]
public sealed class BodyPartDamageEffectsTests
{
    private const int EntityId = 0;

    private static BodyPartTestWorld CreateWorld(string name, BodyPartType type, ushort maximumHealth, bool isVital, float currentHealth)
    {
        var world = new BodyPartTestWorld(new BodyPartTemplate(name, type, 0, maximumHealth, isVital));
        world.SetHealth(EntityId, 0, currentHealth);
        return world;
    }

    private static BodyPartView PartOf(BodyPartTestWorld world)
    {
        Assert.IsTrue(world.BodyParts.TryGet(EntityId, 0, out var part));
        return part;
    }

    [TestMethod]
    public void ApplyToPart_HitLandsAtZero_DisablesAndSetsFreshLockout()
    {
        var world = CreateWorld("Arm", BodyPartType.Arm, maximumHealth: 20, isVital: false, currentHealth: 5);

        BodyPartDamageEffects.ApplyToPart(world.BodyParts, EntityId, partId: 0, statModifiers: null, amount: 10, now: 0);

        var part = PartOf(world);
        Assert.AreEqual(0f, part.CurrentHealth);
        Assert.IsTrue(part.IsDisabled);
        Assert.AreEqual((uint)(10 * GameTiming.FramesPerSecond), part.RegenLockedUntilFrame);
    }

    /// <summary>The lockout re-arms on every hit that leaves a part at 0, not only the first transition into 0 -- a second hit against an already-disabled part (e.g. a burning part's own repeat DoT tick) must not let the lockout quietly keep counting down from the first hit.</summary>
    [TestMethod]
    public void ApplyToPart_SecondHitAgainstAlreadyZeroPart_RearmsLockoutFromFresh()
    {
        var world = CreateWorld("Arm", BodyPartType.Arm, maximumHealth: 20, isVital: false, currentHealth: 5);

        BodyPartDamageEffects.ApplyToPart(world.BodyParts, EntityId, partId: 0, statModifiers: null, amount: 10, now: 0);

        // Time passes -- the second hit lands 100 frames later, and its lockout runs from then.
        BodyPartDamageEffects.ApplyToPart(world.BodyParts, EntityId, partId: 0, statModifiers: null, amount: 1, now: 100);

        var part = PartOf(world);
        Assert.AreEqual(0f, part.CurrentHealth);
        Assert.IsTrue(part.IsDisabled);
        Assert.AreEqual((uint)(100 + (10 * GameTiming.FramesPerSecond)), part.RegenLockedUntilFrame, "The second 0-landing hit must reset the lockout to a fresh 10 seconds from when it landed.");
    }

    [TestMethod]
    public void ApplyToPart_HitDoesNotReachZero_DoesNotDisableOrSetLockout()
    {
        var world = CreateWorld("Torso", BodyPartType.Torso, maximumHealth: 60, isVital: true, currentHealth: 60);

        BodyPartDamageEffects.ApplyToPart(world.BodyParts, EntityId, partId: 0, statModifiers: null, amount: 10, now: 0);

        var part = PartOf(world);
        Assert.AreEqual(50f, part.CurrentHealth);
        Assert.IsFalse(part.IsDisabled);
        Assert.AreEqual(0u, part.RegenLockedUntilFrame);
    }

    [TestMethod]
    public void ApplyToPart_ClampsAtZero_DoesNotGoNegative()
    {
        var world = CreateWorld("Arm", BodyPartType.Arm, maximumHealth: 20, isVital: false, currentHealth: 3);

        BodyPartDamageEffects.ApplyToPart(world.BodyParts, EntityId, partId: 0, statModifiers: null, amount: 100, now: 0);

        Assert.AreEqual(0f, PartOf(world).CurrentHealth);
    }

    /// <summary>The first hit is what creates an entity's body-part state at all -- before it, every part reads as full from its race's templates.</summary>
    [TestMethod]
    public void ApplyToPart_FirstHit_CreatesTheStateWithEveryOtherPartStillFull()
    {
        var world = new BodyPartTestWorld(
            new BodyPartTemplate("Head", BodyPartType.Head, 5, 30, IsVital: true),
            new BodyPartTemplate("Arm", BodyPartType.Arm, 3, 20, IsVital: false));
        world.Give(EntityId);
        Assert.IsFalse(world.States.Has(EntityId));

        BodyPartDamageEffects.ApplyToPart(world.BodyParts, EntityId, partId: 1, statModifiers: null, amount: 5, now: 0);

        Assert.IsTrue(world.States.Has(EntityId));
        Assert.IsTrue(world.BodyParts.TryGet(EntityId, 0, out var head));
        Assert.AreEqual(30f, head.CurrentHealth);
        Assert.IsTrue(world.BodyParts.TryGet(EntityId, 1, out var arm));
        Assert.AreEqual(15f, arm.CurrentHealth);
    }
}
