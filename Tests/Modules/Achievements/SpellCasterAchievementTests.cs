using Engine.ECS.Context;
using Engine.ECS.Entities;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.Achievements;
using Game.Modules.Achievements.Components;
using Game.Modules.Achievements.Definitions;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Definitions;
using Game.Modules.Inventory;
using Game.World;

namespace Tests.Modules.Achievements;

/// <summary>Exercises SpellCasterAchievement end-to-end through the real AchievementModule, mirroring KilledAMobAchievementTests' own Build pattern.</summary>
[TestClass]
public sealed class SpellCasterAchievementTests
{
    private static readonly Guid SpellCasterAchievementId = new SpellCasterAchievement().Id;

    private static (EcsContext EcsContext, EventBus EventBus, Game.World.World World) Build()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(5, 5, 1)));

        return (build.EcsContext, build.Context.EventBus, build.World);
    }

    private static ActivationUsedEvent UsedAction(int entityId, ActionDefinition action) =>
        new(entityId, ActivatableKind.Action, action.Id, action.Tags);

    [TestMethod]
    public void PlayerReadsAScroll_UnlocksSpellCaster()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        var scroll = TestContent.Item(BuiltInItemIds.ScrollOfHealing);

        eventBus.Publish(new ActivationUsedEvent(playerEntityId, ActivatableKind.Item, scroll.Id, scroll.Tags));

        Assert.IsTrue(AchievementQueries.HasEarned(ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(), playerEntityId, SpellCasterAchievementId));
    }

    [TestMethod]
    public void PlayerDrinksAPotion_DoesNotUnlockSpellCaster()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        var potion = TestContent.Item(BuiltInItemIds.HealthPotion);

        eventBus.Publish(new ActivationUsedEvent(playerEntityId, ActivatableKind.Item, potion.Id, potion.Tags));

        Assert.IsFalse(AchievementQueries.HasEarned(ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(), playerEntityId, SpellCasterAchievementId));
    }

    [TestMethod]
    public void PlayerActivatesSpellAction_UnlocksSpellCaster()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;

        eventBus.Publish(UsedAction(playerEntityId, TestContent.Action(BuiltInActionIds.Heal)));

        Assert.IsTrue(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            SpellCasterAchievementId));
    }

    [TestMethod]
    public void PlayerActivatesOtherSpellAction_UnlocksSpellCaster()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;

        eventBus.Publish(UsedAction(playerEntityId, TestContent.Action(BuiltInActionIds.MagicMissile)));

        Assert.IsTrue(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            SpellCasterAchievementId));
    }

    [TestMethod]
    public void PlayerActivatesNonSpellAction_DoesNotUnlockSpellCaster()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;

        eventBus.Publish(UsedAction(playerEntityId, TestContent.Action(BuiltInActionIds.QuickAttack)));

        Assert.IsFalse(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            SpellCasterAchievementId));
    }

    [TestMethod]
    public void NonPlayerActivatesSpellAction_DoesNotUnlockSpellCaster()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        var npcEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;

        eventBus.Publish(UsedAction(npcEntityId, TestContent.Action(BuiltInActionIds.Heal)));

        Assert.IsFalse(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            SpellCasterAchievementId));
    }

    [TestMethod]
    public void PlayerActivatesASpellMasteredFromAScroll_UnlocksSpellCaster()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(5, 5, 1)));
        var componentManager = build.EcsContext.ComponentManager;
        var playerEntityId = build.EcsContext.EntityManager.CreateEntity();
        build.World.PlayerEntityId = playerEntityId;
        var spellId = Guid.NewGuid();
        var healingScroll = TestContent.Item(BuiltInItemIds.ScrollOfHealing);
        var scroll = healingScroll with { Activator = ((ScrollActivator)healingScroll.Activator!) with { SpellId = spellId } };

        for (var i = 0; i < ScrollMasteryEffects.MasteryThreshold; i++)
        {
            ScrollMasteryEffects.RecordUsage(componentManager, build.Context.EventBus, build.Context.Actions, scroll, playerEntityId, spellId);
        }

        build.Context.EventBus.Publish(UsedAction(playerEntityId, build.Context.Actions.Get(spellId)));

        Assert.IsTrue(AchievementQueries.HasEarned(componentManager.GetMultiPool<AchievementUnlockedComponent>(), playerEntityId, SpellCasterAchievementId));
    }
}
