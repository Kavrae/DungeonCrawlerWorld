using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Mana.Components;
using Game.Tags;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class ActivationQueriesTests
{
    private const int EntityId = 1;
    private const long Now = 100;

    private static readonly Guid SpellId = Guid.Parse("0b3c1f7e-5a24-4d8e-9c61-2e7f4a9b3d15");
    private static readonly Guid MeleeSpellId = Guid.Parse("4e8a2d6c-1b93-4f07-a5c2-9d3e7b1f6a80");
    private static readonly Guid FreeCastId = Guid.Parse("9c5d3b1a-7e24-4f86-b0a3-6d2e8c4f1b79");

    private static ActionDefinition Spell(ushort manaCost, ushort? cooldownFrames = null) => new(
        SpellId, "Test Spell", null, "s", Color.White, [GameTags.TargetingSelf],
        Effects: [ActionEffect.None],
        Activator: new SpellActivator(new TargetingSpec(TargetShape.Self, Range: 0), new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: cooldownFrames), manaCost));

    private static ActionDefinition MeleeSpell(ushort manaCost) => new(
        MeleeSpellId, "Test Melee Spell", null, "m", Color.White, [GameTags.DeliveryMelee],
        Effects: [ActionEffect.None],
        Activator: new SpellActivator(new TargetingSpec(TargetShape.Adjacent, Range: 0), new ActionTiming(ActionTimingCategory.Immediate), manaCost));

    private static ActionDefinition FreeCast(ushort cooldownFrames) => new(
        FreeCastId, "Test Free Cast", null, "f", Color.White, [GameTags.TargetingSelf],
        Effects: [ActionEffect.None],
        Activator: new DirectAction(new TargetingSpec(TargetShape.Self, Range: 0), new ActionTiming(ActionTimingCategory.FreeCast, CooldownFrames: cooldownFrames)));

    private static ComponentManager Components(float currentMana = 10f, bool meleeDisabled = false, uint lockedUntilFrame = 0)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(20, 10));
        componentManager.Merge(EntityId, new ManaComponent(currentMana, 100f));
        componentManager.Merge(EntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, unlockedAtFrame: lockedUntilFrame));
        if (meleeDisabled)
        {
            componentManager.Merge(EntityId, new MeleeDisabledComponent());
        }

        return componentManager;
    }

    private static ActivationBlocker BlockerOf(ComponentManager componentManager, ActionDefinition action) =>
        ActivationQueries.GetBlocker(EntityId, action.Activator, action.Tags, componentManager.GetPackedPool<ManaComponent>(), componentManager.GetPackedPool<MeleeDisabledComponent>());

    [TestMethod]
    public void ZeroCostNonMeleeAction_IsNotBlocked()
    {
        Assert.AreEqual(ActivationBlocker.None, BlockerOf(Components(currentMana: 0f, meleeDisabled: true), Spell(manaCost: 0)));
    }

    [TestMethod]
    public void NoActivator_IsNotActivatable()
    {
        var componentManager = Components();

        var blocker = ActivationQueries.GetBlocker(EntityId, activator: null, [], componentManager.GetPackedPool<ManaComponent>(), componentManager.GetPackedPool<MeleeDisabledComponent>());

        Assert.AreEqual(ActivationBlocker.NotActivatable, blocker);
    }

    [TestMethod]
    public void SpellCostingMoreThanCurrentMana_IsNotEnoughMana()
    {
        Assert.AreEqual(ActivationBlocker.NotEnoughMana, BlockerOf(Components(currentMana: 4f), Spell(manaCost: 5)));
        Assert.AreEqual(ActivationBlocker.None, BlockerOf(Components(currentMana: 5f), Spell(manaCost: 5)));
    }

    [TestMethod]
    public void UnarmedMeleeAction_IsBlockedByMeleeDisabled()
    {
        Assert.AreEqual(ActivationBlocker.MeleeDisabled, BlockerOf(Components(meleeDisabled: true), QuickAttackAction.Build()));
        Assert.AreEqual(ActivationBlocker.None, BlockerOf(Components(), QuickAttackAction.Build()));
    }

    [TestMethod]
    public void MeleeDisabledAndOutOfMana_ReportsMeleeDisabled()
    {
        Assert.AreEqual(ActivationBlocker.MeleeDisabled, BlockerOf(Components(currentMana: 0f, meleeDisabled: true), MeleeSpell(manaCost: 5)));
    }

    [TestMethod]
    public void FramesUntilReady_IsTheLaterOfCooldownAndLock()
    {
        var actionCatalog = new ActionCatalog();
        var action = Spell(manaCost: 0, cooldownFrames: 30);
        actionCatalog.Register(action);

        var cooldownOnly = Components();
        var cooldownOnlyActions = TestActionStateViews.EntityActions(cooldownOnly, actionCatalog);
        cooldownOnlyActions.SetCooldown(EntityId, SpellId, 30, Now);
        Assert.AreEqual(30, ActivationQueries.FramesUntilReady(EntityId, action, cooldownOnlyActions, cooldownOnly.GetPackedPool<ActionLockComponent>(), Now));

        var lockOnly = Components(lockedUntilFrame: (uint)Now + 12);
        var lockOnlyActions = TestActionStateViews.EntityActions(lockOnly, actionCatalog);
        Assert.AreEqual(12, ActivationQueries.FramesUntilReady(EntityId, action, lockOnlyActions, lockOnly.GetPackedPool<ActionLockComponent>(), Now));

        var both = Components(lockedUntilFrame: (uint)Now + 50);
        var bothActions = TestActionStateViews.EntityActions(both, actionCatalog);
        bothActions.SetCooldown(EntityId, SpellId, 30, Now);
        Assert.AreEqual(50, ActivationQueries.FramesUntilReady(EntityId, action, bothActions, both.GetPackedPool<ActionLockComponent>(), Now));
    }

    [TestMethod]
    public void FramesUntilReady_ForAFreeCastAction_IgnoresTheLock()
    {
        var actionCatalog = new ActionCatalog();
        var action = FreeCast(cooldownFrames: 5);
        actionCatalog.Register(action);
        var componentManager = Components(lockedUntilFrame: (uint)Now + 50);
        var actions = TestActionStateViews.EntityActions(componentManager, actionCatalog);

        Assert.AreEqual(0, ActivationQueries.FramesUntilReady(EntityId, action, actions, componentManager.GetPackedPool<ActionLockComponent>(), Now));

        actions.SetCooldown(EntityId, FreeCastId, 5, Now);
        Assert.AreEqual(5, ActivationQueries.FramesUntilReady(EntityId, action, actions, componentManager.GetPackedPool<ActionLockComponent>(), Now));
    }

    [TestMethod]
    public void ActionStateView_ReadsTheEntitysEffectiveAction()
    {
        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(Spell(manaCost: 0));
        var componentManager = Components(currentMana: 3f);
        var view = TestActionStateViews.Over(componentManager, actionCatalog);

        componentManager.Merge(EntityId, new ActionInstanceComponent(SpellId, overrideDefinition: null));
        Assert.AreEqual(ActivationBlocker.None, view.GetActionBlocker(EntityId, SpellId));

        componentManager.GetMultiPool<ActionInstanceComponent>().Remove(EntityId);
        componentManager.Merge(EntityId, new ActionInstanceComponent(SpellId, overrideDefinition: Spell(manaCost: 5)));
        Assert.AreEqual(ActivationBlocker.NotEnoughMana, view.GetActionBlocker(EntityId, SpellId));
    }

    [TestMethod]
    public void ActionStateView_AnActionTheEntityLacks_IsNotBlocked()
    {
        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(MeleeSpell(manaCost: 5));
        var view = TestActionStateViews.Over(Components(currentMana: 0f, meleeDisabled: true), actionCatalog, new ItemCatalog());

        Assert.AreEqual(ActivationBlocker.None, view.GetActionBlocker(EntityId, MeleeSpellId));
    }
}
