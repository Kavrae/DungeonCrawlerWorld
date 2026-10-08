using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;

namespace Tests.Modules.StatusEffects;

/// <summary>An immunity a toggle holds is an instance of its own: it ends only under its key, and leaves every other immunity to the type alone.</summary>
[TestClass]
public sealed class HeldImmunityTests
{
    private const int EntityId = 0;

    private static (ComponentManager Components, Engine.ECS.Components.Stores.MultiComponentPool<StatusEffectImmunityComponent> Immunities) Build()
    {
        var components = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));
        return (components, components.GetMultiPool<StatusEffectImmunityComponent>());
    }

    private static bool IsImmune(ComponentManager components) =>
        StatusEffectImmunity.IsImmune(components, EntityId, StatusEffectType.Poison, default, new EventBus(), TestPlayerQuery.NoPlayer);

    [TestMethod]
    public void HeldImmunity_LastsUntilRevokedUnderItsKey()
    {
        var (components, immunities) = Build();

        StatusEffectImmunityEffects.GrantHeld(immunities, EntityId, StatusEffectType.Poison, heldGrantKey: 7);
        Assert.IsTrue(IsImmune(components));

        StatusEffectImmunityEffects.RevokeHeld(immunities, EntityId, StatusEffectType.Poison, heldGrantKey: 8);
        Assert.IsTrue(IsImmune(components), "Another key ends nothing.");

        StatusEffectImmunityEffects.RevokeHeld(immunities, EntityId, StatusEffectType.Poison, heldGrantKey: 7);
        Assert.IsFalse(IsImmune(components));
    }

    [TestMethod]
    public void RevokingAHeldImmunity_LeavesATimedOneOfTheSameType()
    {
        var (components, immunities) = Build();
        StatusEffectImmunityEffects.Grant(immunities, EntityId, StatusEffectType.Poison, FrameDeadline.After(0, 600));
        StatusEffectImmunityEffects.GrantHeld(immunities, EntityId, StatusEffectType.Poison, heldGrantKey: 7);

        StatusEffectImmunityEffects.RevokeHeld(immunities, EntityId, StatusEffectType.Poison, heldGrantKey: 7);

        Assert.AreEqual(1, immunities.CountForEntity(EntityId));
        Assert.IsTrue(IsImmune(components));
    }

    [TestMethod]
    public void TimedGrant_ExtendsTheUnheldImmunity_NotAHeldOne()
    {
        var (_, immunities) = Build();
        StatusEffectImmunityEffects.GrantHeld(immunities, EntityId, StatusEffectType.Poison, heldGrantKey: 7);

        StatusEffectImmunityEffects.Grant(immunities, EntityId, StatusEffectType.Poison, FrameDeadline.After(0, 600));

        Assert.AreEqual(2, immunities.CountForEntity(EntityId), "The timed grant is an unheld instance of its own beside the held one.");
    }
}
