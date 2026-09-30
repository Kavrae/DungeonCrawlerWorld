using Engine.ECS.Entities;
using Engine.ECS.Context;
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
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Actions.Definitions.Spells;
using Game.Modules.Inventory.Definitions;
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

    [TestMethod]
    public void PlayerActivatesSpellAction_UnlocksSpellCaster()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;

        eventBus.Publish(new ActionActivatedEvent(playerEntityId, HealAction.Id));

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

        eventBus.Publish(new ActionActivatedEvent(playerEntityId, MagicMissileAction.Id));

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

        eventBus.Publish(new ActionActivatedEvent(playerEntityId, QuickAttackAction.Id));

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

        eventBus.Publish(new ActionActivatedEvent(npcEntityId, HealAction.Id));

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
        var healingScroll = ScrollOfHealing.Build();
        var scroll = healingScroll with { Activator = ((ScrollActivator)healingScroll.Activator!) with { SpellId = spellId } };

        for (var i = 0; i < ScrollMasteryEffects.MasteryThreshold; i++)
        {
            ScrollMasteryEffects.RecordUsage(componentManager, build.Context.EventBus, build.Context.Actions, scroll, playerEntityId, spellId);
        }

        build.Context.EventBus.Publish(new ActionActivatedEvent(playerEntityId, spellId));

        Assert.IsTrue(AchievementQueries.HasEarned(componentManager.GetMultiPool<AchievementUnlockedComponent>(), playerEntityId, SpellCasterAchievementId));
    }
}
