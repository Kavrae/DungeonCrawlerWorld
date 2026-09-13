using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Burning;
using Game.Modules.Burning.Systems;
using Game.Modules.Health.Components;
using Game.World;

namespace Tests.Modules.Burning;

[TestClass]
public sealed class BodyPartBurningSystemTests
{
    private sealed class FakePlayerQuery(int playerEntityId) : IPlayerQuery
    {
        public int PlayerEntityId { get; } = playerEntityId;
    }

    private static MultiComponentPool<BodyPartBurningTimerComponent> CreateTimerPool() => new(maximumEntityCount: 10, initialCapacity: 8);

    private static MultiComponentPool<BodyPartComponent> CreateBodyPartsPool() => new(maximumEntityCount: 10, initialCapacity: 8);

    private static PackedComponentPool<SimpleHealthComponent> CreateHealthPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static EngineTime Frame(long frame) => new(default, default, false, frame);

    [TestMethod]
    public void Update_AtTickFrame_DamagesOnlyItsOwnNamedPart()
    {
        var timers = CreateTimerPool();
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Head", BodyPartType.Head, partId: 0, verticalPosition: 5, currentHealth: 30, maximumHealth: 30, isVital: true));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, partId: 1, verticalPosition: 4, currentHealth: 60, maximumHealth: 60, isVital: true));
        timers.Add(0, new BodyPartBurningTimerComponent(partId: 1, stackCount: 4, nextTickFrame: 1, StatusEffectSource.Admin));
        var system = new BodyPartBurningSystem(timers, bodyParts, CreateHealthPool(), new EventBus(), new FakePlayerQuery(0));

        system.Update(Frame(1), 0);

        var headDenseIndex = BodyPartSelectionFindByName(bodyParts, 0, "Head");
        var torsoDenseIndex = BodyPartSelectionFindByName(bodyParts, 0, "Torso");
        Assert.AreEqual(30f, bodyParts.GetReadonlyByDenseIndex(headDenseIndex).CurrentHealth, "Head must be untouched -- the burn is scoped to Torso's own PartId.");
        Assert.AreEqual(56f, bodyParts.GetReadonlyByDenseIndex(torsoDenseIndex).CurrentHealth);
        Assert.AreEqual(3, FindPartStackCount(timers, 0, 1), "One stack removed by this tick, leaving 3 of the original 4.");
    }

    [TestMethod]
    public void Update_LastStackConsumed_RemovesTimerAndStacks()
    {
        var timers = CreateTimerPool();
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, partId: 0, verticalPosition: 4, currentHealth: 60, maximumHealth: 60, isVital: true));
        timers.Add(0, new BodyPartBurningTimerComponent(partId: 0, stackCount: 1, nextTickFrame: 1, StatusEffectSource.Admin));
        var system = new BodyPartBurningSystem(timers, bodyParts, CreateHealthPool(), new EventBus(), new FakePlayerQuery(0));

        system.Update(Frame(1), 0);

        Assert.IsFalse(timers.Has(0));
        Assert.AreEqual(0, FindPartStackCount(timers, 0, 0));
    }

    [TestMethod]
    public void Update_TwoPartsBurningConcurrently_EachDamagesOnlyItsOwnPart()
    {
        var timers = CreateTimerPool();
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Left Foot", BodyPartType.Foot, partId: 0, verticalPosition: 0, currentHealth: 10, maximumHealth: 10, isVital: false));
        bodyParts.Add(0, new BodyPartComponent("Right Foot", BodyPartType.Foot, partId: 1, verticalPosition: 0, currentHealth: 10, maximumHealth: 10, isVital: false));

        // Constructed before either Add below -- both instances reach the wheel through the pool's
        // own change notification, each keyed by its PartId, not by a scan at construction.
        var system = new BodyPartBurningSystem(timers, bodyParts, CreateHealthPool(), new EventBus(), new FakePlayerQuery(0));
        timers.Add(0, new BodyPartBurningTimerComponent(partId: 0, stackCount: 2, nextTickFrame: 1, StatusEffectSource.Admin));
        timers.Add(0, new BodyPartBurningTimerComponent(partId: 1, stackCount: 1, nextTickFrame: 1, StatusEffectSource.Admin));

        system.Update(Frame(1), 0);

        var leftFootDenseIndex = BodyPartSelectionFindByName(bodyParts, 0, "Left Foot");
        var rightFootDenseIndex = BodyPartSelectionFindByName(bodyParts, 0, "Right Foot");
        Assert.AreEqual(8f, bodyParts.GetReadonlyByDenseIndex(leftFootDenseIndex).CurrentHealth, "Left Foot had 2 stacks -- 2 damage.");
        Assert.AreEqual(9f, bodyParts.GetReadonlyByDenseIndex(rightFootDenseIndex).CurrentHealth, "Right Foot had 1 stack -- 1 damage, independently.");

        // Left Foot had 2 stacks (1 remains after this tick, timer entry kept); Right Foot had
        // only 1 stack (fully consumed, timer entry removed) -- Left Foot's own entry keeps the pool non-empty for entity 0.
        Assert.IsTrue(timers.Has(0));
    }

    /// <summary>Regression test: a Foot's own 10 HP easily survives a lightly-stacked burn tick, so BodyPartDamageEffects.ApplyToPart's own 0-only lockout never engages -- without BodyPartBurningSystem also calling ResetRegenLockout unconditionally, the part would have zero regen protection the instant the fire's stacks ran out.</summary>
    [TestMethod]
    public void Update_TickDoesNotReduceCurrentHealthToZero_StillRefreshesRegenLockout()
    {
        var timers = CreateTimerPool();
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Left Foot", BodyPartType.Foot, partId: 0, verticalPosition: 0, currentHealth: 10, maximumHealth: 10, isVital: false));
        timers.Add(0, new BodyPartBurningTimerComponent(partId: 0, stackCount: 1, nextTickFrame: 1, StatusEffectSource.Admin));
        var system = new BodyPartBurningSystem(timers, bodyParts, CreateHealthPool(), new EventBus(), new FakePlayerQuery(0));

        system.Update(Frame(1), 0);

        var part = bodyParts.GetReadonlyByDenseIndex(BodyPartSelectionFindByName(bodyParts, 0, "Left Foot"));
        Assert.AreEqual(9f, part.CurrentHealth, "Sanity check: this tick's 1 damage doesn't reach 0.");
        Assert.IsFalse(part.IsDisabled);
        Assert.IsGreaterThan(0u, part.RegenLockedUntilFrame, "Even a non-lethal burn tick must refresh the lockout, or the part regens instantly once the fire's stacks run out.");
    }

    [TestMethod]
    public void Update_PartDropsToZero_DisablesPartAndDoesNotThrow()
    {
        var timers = CreateTimerPool();
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Left Foot", BodyPartType.Foot, partId: 0, verticalPosition: 0, currentHealth: 2, maximumHealth: 10, isVital: false));
        timers.Add(0, new BodyPartBurningTimerComponent(partId: 0, stackCount: 2, nextTickFrame: 1, StatusEffectSource.Admin));
        var system = new BodyPartBurningSystem(timers, bodyParts, CreateHealthPool(), new EventBus(), new FakePlayerQuery(0));

        system.Update(Frame(1), 0);

        var part = bodyParts.GetReadonlyByDenseIndex(BodyPartSelectionFindByName(bodyParts, 0, "Left Foot"));
        Assert.AreEqual(0f, part.CurrentHealth);
        Assert.IsTrue(part.IsDisabled);
    }

    /// <summary>Each part's burn is its own wheel entry keyed by PartId -- one part ticking (and re-arming) never moves or fires the other's.</summary>
    [TestMethod]
    public void Update_TwoPartsOnDifferentFrames_EachTicksOnItsOwnFrame()
    {
        var timers = CreateTimerPool();
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Left Foot", BodyPartType.Foot, partId: 0, verticalPosition: 0, currentHealth: 10, maximumHealth: 10, isVital: false));
        bodyParts.Add(0, new BodyPartComponent("Right Foot", BodyPartType.Foot, partId: 1, verticalPosition: 0, currentHealth: 10, maximumHealth: 10, isVital: false));
        timers.Add(0, new BodyPartBurningTimerComponent(partId: 0, stackCount: 3, nextTickFrame: 5, StatusEffectSource.Admin));
        timers.Add(0, new BodyPartBurningTimerComponent(partId: 1, stackCount: 3, nextTickFrame: 20, StatusEffectSource.Admin));
        var system = new BodyPartBurningSystem(timers, bodyParts, CreateHealthPool(), new EventBus(), new FakePlayerQuery(0));

        for (var frame = 0; frame <= 5; frame++)
        {
            system.Update(Frame(frame), 0);
        }

        Assert.AreEqual(7f, bodyParts.GetReadonlyByDenseIndex(BodyPartSelectionFindByName(bodyParts, 0, "Left Foot")).CurrentHealth);
        Assert.AreEqual(10f, bodyParts.GetReadonlyByDenseIndex(BodyPartSelectionFindByName(bodyParts, 0, "Right Foot")).CurrentHealth);
        Assert.AreEqual(5u + BurningEffects.TickIntervalFrames, FindPartNextTickFrame(timers, 0, 0), "Re-armed one interval after its own tick.");
        Assert.AreEqual(20u, FindPartNextTickFrame(timers, 0, 1), "The other part's deadline is untouched.");

        for (var frame = 6; frame <= 20; frame++)
        {
            system.Update(Frame(frame), 0);
        }

        Assert.AreEqual(7f, bodyParts.GetReadonlyByDenseIndex(BodyPartSelectionFindByName(bodyParts, 0, "Right Foot")).CurrentHealth);
    }

    private static uint FindPartNextTickFrame(MultiComponentPool<BodyPartBurningTimerComponent> timers, int entityId, byte partId)
    {
        for (var denseIndex = timers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = timers.GetNextDenseIndex(denseIndex))
        {
            var timer = timers.GetReadonlyByDenseIndex(denseIndex);
            if (timer.PartId == partId)
            {
                return timer.NextTickFrame;
            }
        }

        return 0;
    }

    private static int BodyPartSelectionFindByName(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, string name)
    {
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            if (bodyParts.GetReadonlyByDenseIndex(denseIndex).Name == name)
            {
                return denseIndex;
            }
        }

        return -1;
    }

    private static int FindPartStackCount(MultiComponentPool<BodyPartBurningTimerComponent> timers, int entityId, byte partId)
    {
        for (var denseIndex = timers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = timers.GetNextDenseIndex(denseIndex))
        {
            var timer = timers.GetReadonlyByDenseIndex(denseIndex);
            if (timer.PartId == partId)
            {
                return timer.StackCount;
            }
        }

        return 0;
    }
}
