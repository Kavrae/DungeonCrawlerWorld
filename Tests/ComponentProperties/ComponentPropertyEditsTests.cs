using Engine.ECS.Properties;
using Engine.Math;
using Engine.ECS.Systems;
using Game.Admin;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.StatModifiers;
using Game.Bootstrap;
using Game.Modules.Actions.Components;
using Game.Modules.Auras.Components;
using Game.Modules.Burning.Components;
using Game.Modules.Mana.Components;
using Game.World;

namespace Tests.ComponentProperties;

/// <summary>Admin Mode's edits: written through the table by entity key, kept for revert, refused -- with nothing written -- for a destroyed entity, a changed instance, a read-only value or a link the relationship won't take.</summary>
[TestClass]
public sealed class ComponentPropertyEditsTests
{
    private static GameSession Bootstrap() =>
        GameBootstrapper.Build(ValidatedMods.None, new Map(new Vector3Int(20, 20, 3)), TestContent.Layers, sessionSeed: 1, initialEntityCapacity: 1_000, initialComponentCapacity: 100);

    private static ComponentPropertyEdits EditsOf(GameSession session) =>
        new(session.EcsContext.ComponentManager, session.EcsContext.SystemManager, session.SimulationClock);

    private static ComponentPropertyTarget Target<TComponent>(GameSession session, int entityId, int instance = 0) =>
        EditsOf(session).TargetOf(session.EcsContext.ComponentManager.Keys.GetKey(entityId), typeof(TComponent), instance);

    private static ComponentPropertyTarget TargetHolding<TComponent>(GameSession session, ComponentPropertyEdits edits, int entityId, TComponent value)
        where TComponent : struct
    {
        var key = session.EcsContext.ComponentManager.Keys.GetKey(entityId);
        for (var instance = 0; ; instance++)
        {
            var target = edits.TargetOf(key, typeof(TComponent), instance);
            if (target.Value is null || target.Value.Equals(value))
            {
                return target;
            }
        }
    }

    [TestMethod]
    public void AnEdit_WritesTheValueKeepsWhatItReplacedAndRevertsIt()
    {
        var session = Bootstrap();
        var mana = session.EcsContext.ComponentManager.GetPackedPool<ManaComponent>();
        var entityId = session.EcsContext.EntityManager.CreateEntity();
        mana.Add(entityId, new ManaComponent(currentMana: 40, baseMaximumMana: 50));
        var edits = EditsOf(session);

        var applied = edits.Apply(Target<ManaComponent>(session, entityId), [0], ComponentPropertyValue.FromNumber(0));

        Assert.IsTrue(applied.Applied, applied.Refusal);
        Assert.AreEqual(0f, mana.GetReadonly(entityId).CurrentMana);
        Assert.AreEqual(40d, applied.Edit!.Previous.Number);
        Assert.AreEqual("CurrentMana", applied.Edit.PropertyName);
        Assert.AreSame(applied.Edit, edits.LatestFor(Target<ManaComponent>(session, entityId), [0]));
        Assert.IsFalse(edits.FramesHavePassedSince(applied.Edit));

        var reverted = edits.Revert(applied.Edit);

        Assert.IsTrue(reverted.Applied, reverted.Refusal);
        Assert.AreEqual(40f, mana.GetReadonly(entityId).CurrentMana);
        Assert.IsEmpty(edits.Applied);
    }

    [TestMethod]
    public void ADestroyedEntity_IsRefusedAndItsEditsForgotten_WhateverNowHoldsItsIdIsUntouched()
    {
        var session = Bootstrap();
        var mana = session.EcsContext.ComponentManager.GetPackedPool<ManaComponent>();
        var entityId = session.EcsContext.EntityManager.CreateEntity();
        mana.Add(entityId, new ManaComponent(40, 50));
        var edits = EditsOf(session);
        var target = Target<ManaComponent>(session, entityId);
        var edit = edits.Apply(target, [0], ComponentPropertyValue.FromNumber(5)).Edit!;

        session.EcsContext.EntityManager.DestroyEntity(entityId);
        var successor = session.EcsContext.EntityManager.CreateEntity();
        mana.Add(successor, new ManaComponent(30, 50));

        Assert.AreEqual(entityId, successor, "Precondition: the id was recycled.");
        Assert.AreEqual(ComponentPropertyRefusals.EntityDestroyed, edits.RevertRefusal(edit));
        Assert.AreEqual(ComponentPropertyRefusals.EntityDestroyed, edits.Revert(edit).Refusal);
        Assert.IsEmpty(edits.Applied, "A revert lets go of the destroyed entity's edits.");
        Assert.IsNull(edits.Latest());
        Assert.AreEqual(ComponentPropertyRefusals.EntityDestroyed, edits.Apply(target, [0], ComponentPropertyValue.FromNumber(1)).Refusal);
        Assert.AreEqual(30f, mana.GetReadonly(successor).CurrentMana);
    }

    [TestMethod]
    public void Latest_ForgetsTheEditsOfADestroyedEntity_AndOffersTheOneBeforeThem()
    {
        var session = Bootstrap();
        var mana = session.EcsContext.ComponentManager.GetPackedPool<ManaComponent>();
        var keptEntityId = session.EcsContext.EntityManager.CreateEntity();
        var destroyedEntityId = session.EcsContext.EntityManager.CreateEntity();
        mana.Add(keptEntityId, new ManaComponent(40, 50));
        mana.Add(destroyedEntityId, new ManaComponent(40, 50));
        var edits = EditsOf(session);
        var keptEdit = edits.Apply(Target<ManaComponent>(session, keptEntityId), [0], ComponentPropertyValue.FromNumber(5)).Edit!;
        edits.Apply(Target<ManaComponent>(session, destroyedEntityId), [0], ComponentPropertyValue.FromNumber(6));

        session.EcsContext.EntityManager.DestroyEntity(destroyedEntityId);

        Assert.AreSame(keptEdit, edits.Latest());
        Assert.IsNull(edits.RevertRefusal(keptEdit));
    }

    [TestMethod]
    public void AnInstanceEditedToEqualAnother_KeepsItsEditOnItsOwnPositionOnly()
    {
        var session = Bootstrap();
        var cooldowns = session.EcsContext.ComponentManager.GetMultiPool<ActionCooldownComponent>();
        var entityId = session.EcsContext.EntityManager.CreateEntity();
        var actionId = Guid.NewGuid();
        cooldowns.Add(entityId, new ActionCooldownComponent(actionId, 20));
        cooldowns.Add(entityId, new ActionCooldownComponent(actionId, 20));
        var edits = EditsOf(session);
        var key = session.EcsContext.ComponentManager.Keys.GetKey(entityId);

        Assert.IsTrue(edits.Apply(edits.TargetOf(key, typeof(ActionCooldownComponent), 1), [1], ComponentPropertyValue.FromInteger(500)).Applied);
        var back = edits.Apply(edits.TargetOf(key, typeof(ActionCooldownComponent), 1), [1], ComponentPropertyValue.FromInteger(20));

        Assert.IsTrue(back.Applied, back.Refusal);
        Assert.AreSame(back.Edit, edits.LatestFor(edits.TargetOf(key, typeof(ActionCooldownComponent), 1), [1]));
        Assert.IsNull(edits.LatestFor(edits.TargetOf(key, typeof(ActionCooldownComponent), 0), [1]));
    }

    [TestMethod]
    public void AnAbilityScoreBaseOutsideItsRange_IsRefused()
    {
        var session = Bootstrap();
        var components = session.EcsContext.ComponentManager;
        var entityId = session.EcsContext.EntityManager.CreateEntity();
        AbilityScoreEffects.Grant(components, entityId, AbilityScoreType.Strength, 10);
        var edits = ComponentPropertyEditCaches.For(session);
        var target = Target<AbilityScoresComponent>(session, entityId);

        Assert.IsFalse(edits.Apply(target, [0, (int)AbilityScoreType.Strength], ComponentPropertyValue.FromInteger(AbilityScoreMath.MinimumBaseValue - 1)).Applied);
        Assert.IsFalse(edits.Apply(target, [0, (int)AbilityScoreType.Strength], ComponentPropertyValue.FromInteger(AbilityScoreMath.MaximumBaseValue + 1)).Applied);
        Assert.IsTrue(components.GetPackedPool<AbilityScoresComponent>().GetReadonly(entityId).TryGet(AbilityScoreType.Strength, out var strength));
        Assert.AreEqual(10, strength.BaseValue);
    }

    [TestMethod]
    public void AKeyedTimer_IsFoundByItsKeyWhereverItNowSits()
    {
        var session = Bootstrap();
        var burns = session.EcsContext.ComponentManager.GetMultiPool<BodyPartBurningTimerComponent>();
        var entityId = session.EcsContext.EntityManager.CreateEntity();
        burns.Add(entityId, new BodyPartBurningTimerComponent(partId: 2, stackCount: 1, nextTickFrame: 100, ActionSource.Admin));
        burns.Add(entityId, new BodyPartBurningTimerComponent(partId: 5, stackCount: 1, nextTickFrame: 100, ActionSource.Admin));
        var edits = EditsOf(session);
        var target = new ComponentPropertyTarget(session.EcsContext.ComponentManager.Keys.GetKey(entityId), typeof(BodyPartBurningTimerComponent), Instance: 1, TimerKey: 5);

        Assert.IsTrue(burns.RemoveFirst(entityId, new PartMatcher(2)));
        var result = edits.Apply(target, [2], ComponentPropertyValue.FromInteger(700));

        Assert.IsTrue(result.Applied, result.Refusal);
        Assert.IsTrue(burns.TryGetFirst(entityId, new PartMatcher(5), out var burn));
        Assert.AreEqual(700u, burn.NextTickFrame);
    }

    [TestMethod]
    public void AnUnkeyedInstanceAmongSeveral_IsFoundByItsValue_WhenOthersCameAndWentKeepingTheCount()
    {
        var session = Bootstrap();
        var cooldowns = session.EcsContext.ComponentManager.GetMultiPool<ActionCooldownComponent>();
        var entityId = session.EcsContext.EntityManager.CreateEntity();
        var first = new ActionCooldownComponent(Guid.NewGuid(), 10);
        var edited = new ActionCooldownComponent(Guid.NewGuid(), 20);
        var later = new ActionCooldownComponent(Guid.NewGuid(), 30);
        cooldowns.Add(entityId, first);
        cooldowns.Add(entityId, edited);
        var edits = EditsOf(session);
        var target = TargetHolding(session, edits, entityId, edited);

        cooldowns.Add(entityId, later);
        Assert.IsTrue(cooldowns.RemoveFirst(entityId, new CooldownMatcher(first.ActionId)));
        var result = edits.Apply(target, [1], ComponentPropertyValue.FromInteger(500));

        Assert.IsTrue(result.Applied, result.Refusal);
        Assert.IsTrue(cooldowns.TryGetFirst(entityId, new CooldownMatcher(edited.ActionId), out var written));
        Assert.AreEqual(500u, written.ReadyAtFrame);
        Assert.IsTrue(cooldowns.TryGetFirst(entityId, new CooldownMatcher(later.ActionId), out var untouched));
        Assert.AreEqual(30u, untouched.ReadyAtFrame);
    }

    [TestMethod]
    public void EditsOfTwoPropertiesOfOneInstanceAmongSeveral_EachRevertAfterItsPositionMoves()
    {
        var session = Bootstrap();
        var cooldowns = session.EcsContext.ComponentManager.GetMultiPool<ActionCooldownComponent>();
        var entityId = session.EcsContext.EntityManager.CreateEntity();
        var edited = new ActionCooldownComponent(Guid.NewGuid(), 20);
        cooldowns.Add(entityId, edited);
        var edits = EditsOf(session);
        var applied = edits.Apply(TargetHolding(session, edits, entityId, edited), [1], ComponentPropertyValue.FromInteger(500)).Edit!;

        cooldowns.Add(entityId, new ActionCooldownComponent(Guid.NewGuid(), 30));
        var moved = TargetHolding(session, edits, entityId, new ActionCooldownComponent(edited.ActionId, 500));

        Assert.AreSame(applied, edits.LatestFor(moved, [1]));
        Assert.IsTrue(edits.Revert(applied).Applied);
        Assert.IsTrue(cooldowns.TryGetFirst(entityId, new CooldownMatcher(edited.ActionId), out var reverted));
        Assert.AreEqual(20u, reverted.ReadyAtFrame);
        Assert.IsEmpty(edits.Applied);
    }

    [TestMethod]
    public void AnUnkeyedInstanceSomethingElseChanged_IsRefused()
    {
        var session = Bootstrap();
        var cooldowns = session.EcsContext.ComponentManager.GetMultiPool<ActionCooldownComponent>();
        var entityId = session.EcsContext.EntityManager.CreateEntity();
        var edited = new ActionCooldownComponent(Guid.NewGuid(), 20);
        cooldowns.Add(entityId, edited);
        cooldowns.Add(entityId, new ActionCooldownComponent(Guid.NewGuid(), 30));
        var edits = EditsOf(session);
        var applied = edits.Apply(TargetHolding(session, edits, entityId, edited), [1], ComponentPropertyValue.FromInteger(500)).Edit!;

        Assert.IsTrue(cooldowns.RemoveFirst(entityId, new CooldownMatcher(edited.ActionId)));
        cooldowns.Add(entityId, new ActionCooldownComponent(edited.ActionId, 900));

        Assert.AreEqual(ComponentPropertyRefusals.InstancesChanged, edits.Revert(applied).Refusal);
    }

    [TestMethod]
    public void ANonFiniteNumber_IsRefused()
    {
        var session = Bootstrap();
        var mana = session.EcsContext.ComponentManager.GetPackedPool<ManaComponent>();
        var entityId = session.EcsContext.EntityManager.CreateEntity();
        mana.Add(entityId, new ManaComponent(40, 50));
        var edits = EditsOf(session);

        Assert.AreEqual(ComponentPropertyRefusals.ValueRefused, edits.Apply(Target<ManaComponent>(session, entityId), [0], ComponentPropertyValue.FromNumber(double.NaN)).Refusal);
        Assert.AreEqual(ComponentPropertyRefusals.ValueRefused, edits.Apply(Target<ManaComponent>(session, entityId), [0], ComponentPropertyValue.FromNumber(1e300)).Refusal);
        Assert.AreEqual(ComponentPropertyRefusals.ValueRefused, edits.Apply(Target<ManaComponent>(session, entityId), [0], ComponentPropertyValue.Absent).Refusal);
        Assert.AreEqual(40f, mana.GetReadonly(entityId).CurrentMana);
    }

    [TestMethod]
    public void AReadOnlyValue_IsRefusedAndNothingIsKept()
    {
        var session = Bootstrap();
        var cooldowns = session.EcsContext.ComponentManager.GetMultiPool<ActionCooldownComponent>();
        var entityId = session.EcsContext.EntityManager.CreateEntity();
        cooldowns.Add(entityId, new ActionCooldownComponent(Guid.NewGuid(), 10));
        var edits = EditsOf(session);

        var result = edits.Apply(Target<ActionCooldownComponent>(session, entityId), [0], ComponentPropertyValue.FromGuid(Guid.NewGuid()));

        Assert.AreEqual(ComponentPropertyRefusals.ValueRefused, result.Refusal);
        Assert.IsEmpty(edits.Applied);
    }

    [TestMethod]
    public void ALinkTheRelationshipRefuses_IsNeverWritten()
    {
        var session = Bootstrap();
        var components = session.EcsContext.ComponentManager;
        var owner = session.EcsContext.EntityManager.CreateEntity();
        var anchor = session.EcsContext.EntityManager.CreateEntity();
        components.GetRelationship<AuraAnchorOwnerLink>().Link(anchor, new AuraAnchorOwnerLink(components.Keys.GetKey(owner), HeldGrantKey: 3));
        var edits = EditsOf(session);

        var result = edits.Apply(Target<AuraAnchorOwnerLink>(session, anchor), [0], ComponentPropertyValue.FromInteger((long)components.Keys.GetKey(anchor).Value));

        StringAssert.Contains(result.Refusal, "itself");
        Assert.IsTrue(components.GetRelationship<AuraAnchorOwnerLink>().TryGetTargetEntityId(anchor, out var target));
        Assert.AreEqual(owner, target, "The anchor still names its owner.");
        Assert.IsEmpty(edits.Applied);
    }

    [TestMethod]
    public void AnAbilityScoreBaseEdit_RecomputesItsTotalWithTheModifiersOnIt()
    {
        var session = Bootstrap();
        var components = session.EcsContext.ComponentManager;
        var entityId = session.EcsContext.EntityManager.CreateEntity();
        AbilityScoreEffects.Grant(components, entityId, AbilityScoreType.Strength, 10);
        AbilityScoreEffects.GrantModifier(components, entityId, AbilityScoreType.Strength, StatModifierOperation.Additive, StatModifierPolarity.Buff, canModify: true, magnitude: 5, FrameDeadline.Never, ActionSource.Admin);
        var edits = ComponentPropertyEditCaches.For(session);
        var scores = components.GetPackedPool<AbilityScoresComponent>();

        var result = edits.Apply(Target<AbilityScoresComponent>(session, entityId), [0, (int)AbilityScoreType.Strength], ComponentPropertyValue.FromInteger(20));

        Assert.IsTrue(result.Applied, result.Refusal);
        Assert.IsTrue(scores.GetReadonly(entityId).TryGet(AbilityScoreType.Strength, out var strength));
        Assert.AreEqual(20, strength.BaseValue);
        Assert.AreEqual(25, strength.Total);

        edits.Revert(result.Edit!);

        Assert.IsTrue(scores.GetReadonly(entityId).TryGet(AbilityScoreType.Strength, out var reverted));
        Assert.AreEqual(15, reverted.Total);
    }

    private readonly struct CooldownMatcher(Guid actionId) : Engine.ECS.Components.IComponentMatcher<ActionCooldownComponent>
    {
        public bool Matches(in ActionCooldownComponent component) => component.ActionId == actionId;
    }

    private readonly struct PartMatcher(byte partId) : Engine.ECS.Components.IComponentMatcher<BodyPartBurningTimerComponent>
    {
        public bool Matches(in BodyPartBurningTimerComponent component) => component.PartId == partId;
    }
}
