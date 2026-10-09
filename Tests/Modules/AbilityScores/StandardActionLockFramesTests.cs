using Engine.ECS.Components;
using Engine.ECS.Systems;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.AbilityScores;

[TestClass]
public sealed class StandardActionLockFramesTests
{
    private const int EntityId = 0;

    private static ComponentManager CreateRegisteredManager() =>
        BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8));

    private static ComponentManager WithDexterity(ushort dexterityBaseValue)
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, EntityId, AbilityScoreType.Dexterity, dexterityBaseValue);
        return manager;
    }

    private static void GrantActionLockMultiplier(ComponentManager manager, float magnitude) =>
        StatModifierEffects.Apply(manager, EntityId, StatModifierTarget.ActionLockFrames, StatModifierOperation.Multiplicative,
            magnitude < 0 ? StatModifierPolarity.Buff : StatModifierPolarity.Debuff, canModify: false, magnitude, FrameDeadline.Never, ActionSource.Admin);

    [TestMethod]
    public void ComputeFromDexterity_DexterityTotal1_ReturnsMaximumLockFrames() =>
        Assert.AreEqual(StandardActionLockFrames.MaximumLockFrames, StandardActionLockFrames.ComputeFromDexterity(1));

    [TestMethod]
    public void ComputeFromDexterity_DexterityTotal300_ReturnsMinimumLockFrames() =>
        Assert.AreEqual(StandardActionLockFrames.MinimumLockFrames, StandardActionLockFrames.ComputeFromDexterity(300));

    [TestMethod]
    public void ComputeFromDexterity_OutsideTheScoreRange_Clamps()
    {
        Assert.AreEqual(StandardActionLockFrames.MaximumLockFrames, StandardActionLockFrames.ComputeFromDexterity(0));
        Assert.AreEqual(StandardActionLockFrames.MinimumLockFrames, StandardActionLockFrames.ComputeFromDexterity(1000));
    }

    [TestMethod]
    public void ResolveForEntity_LowDexterity_RoundsToTheNearestFrame() =>
        Assert.AreEqual((ushort)30, TestActionLocks.StandardLockOf(WithDexterity(10), EntityId));

    [TestMethod]
    public void ResolveForEntity_MaximumDexterity_ReturnsMinimumLockFrames() =>
        Assert.AreEqual(StandardActionLockFrames.MinimumLockFrames, TestActionLocks.StandardLockOf(WithDexterity(300), EntityId));

    [TestMethod]
    public void ResolveForEntity_NoDexterityScore_ReturnsMaximumLockFrames() =>
        Assert.AreEqual(StandardActionLockFrames.MaximumLockFrames, TestActionLocks.StandardLockOf(CreateRegisteredManager(), EntityId));

    [TestMethod]
    public void ResolveForEntity_DexterityModifier_ChangesTheLock()
    {
        var manager = WithDexterity(1);

        AbilityScoreEffects.GrantModifier(manager, EntityId, AbilityScoreType.Dexterity, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: false, magnitude: 299f, FrameDeadline.Never, ActionSource.Admin);

        Assert.AreEqual(StandardActionLockFrames.MinimumLockFrames, TestActionLocks.StandardLockOf(manager, EntityId));
    }

    [TestMethod]
    public void ResolveForEntity_FasterModifier_ShortensTheLock()
    {
        var manager = WithDexterity(1);

        GrantActionLockMultiplier(manager, -0.1f);

        Assert.AreEqual((ushort)27, TestActionLocks.StandardLockOf(manager, EntityId));
    }

    [TestMethod]
    public void ResolveForEntity_SlowerModifier_LengthensTheLock()
    {
        var manager = WithDexterity(1);

        GrantActionLockMultiplier(manager, 0.8f);

        Assert.AreEqual((ushort)54, TestActionLocks.StandardLockOf(manager, EntityId));
    }

    [TestMethod]
    public void ResolveForEntity_TwoModifiers_AddRatherThanCompound()
    {
        var manager = WithDexterity(1);

        GrantActionLockMultiplier(manager, -0.1f);
        GrantActionLockMultiplier(manager, -0.1f);

        Assert.AreEqual((ushort)24, TestActionLocks.StandardLockOf(manager, EntityId));
    }

    [TestMethod]
    public void ResolveForEntity_ModifiersRemovingTheWholeLock_FloorAtOneFrame()
    {
        var manager = WithDexterity(1);

        GrantActionLockMultiplier(manager, -1f);

        Assert.AreEqual((ushort)1, TestActionLocks.StandardLockOf(manager, EntityId));
    }

    [TestMethod]
    public void ResolveForEntity_ExplicitLockFrames_WinOverTheStandardLock()
    {
        var manager = WithDexterity(1);
        GrantActionLockMultiplier(manager, -0.5f);

        var resolved = StandardActionLockFrames.ResolveForEntity(
            manager.GetPackedPool<AbilityScoresComponent>(),
            manager.GetMultiPool<StatModifierComponent>(),
            EntityId,
            explicitLockFrames: 90);

        Assert.AreEqual((ushort)90, resolved);
    }
}
