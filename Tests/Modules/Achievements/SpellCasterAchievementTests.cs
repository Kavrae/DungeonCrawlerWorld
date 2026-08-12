using Engine.Bootstrap;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.Achievements;
using Game.Modules.Achievements.Components;
using Game.Modules.Achievements.Definitions;
using Game.Modules.Actions.Definitions;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Actions.Definitions.Spells;
using Game.World;

namespace Tests.Modules.Achievements;

/// <summary>Exercises SpellCasterAchievement end-to-end through the real AchievementModule, mirroring KilledAMobAchievementTests' own Build pattern.</summary>
[TestClass]
public sealed class SpellCasterAchievementTests
{
    private static readonly Guid SpellCasterAchievementId = new SpellCasterAchievement().Id;

    private static (EcsContext EcsContext, EventBus EventBus, Game.World.World World) Build()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var eventBus = new EventBus();
        var context = new GameModuleContext(world, new MathUtility(), eventBus) { PlayerQuery = world };

        var coreActionsModule = new CoreActionsModule();
        coreActionsModule.Configure(context);

        var module = new AchievementModule();
        module.Configure(context);

        IReadOnlyList<IModule> modules = [module, coreActionsModule];
        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: 10, initialComponentCapacity: 10, eventBus);

        return (ecsContext, eventBus, world);
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

        eventBus.Publish(new ActionActivatedEvent(playerEntityId, PunchAction.Id));

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
}
