using Engine.Bootstrap;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.Achievements;
using Game.Modules.Achievements.Components;
using Game.Modules.Achievements.Definitions;
using Game.World;

namespace Tests.Modules.Achievements;

/// <summary>Exercises DrinkingProblemAchievement end-to-end through the real AchievementModule, mirroring KilledAMobAchievementTests' own Build pattern.</summary>
[TestClass]
public sealed class DrinkingProblemAchievementTests
{
    private static readonly Guid DrinkingProblemAchievementId = new DrinkingProblemAchievement().Id;

    private static (EcsContext EcsContext, EventBus EventBus, Game.World.World World) Build()
    {
        var build = BuiltInTestModules.BuildModules([new Game.Modules.Lootboxes.LootboxModule(), new AchievementModule()]);

        return (build.EcsContext, build.Context.EventBus, build.World);
    }

    [TestMethod]
    public void PotionCooldownAbusedForThePlayer_UnlocksDrinkingProblem()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;

        eventBus.Publish(new PotionCooldownAbusedEvent(playerEntityId));

        Assert.IsTrue(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            DrinkingProblemAchievementId));
    }

    [TestMethod]
    public void PotionCooldownAbusedForANonPlayerEntity_DoesNotUnlockDrinkingProblem()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        var npcEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;

        eventBus.Publish(new PotionCooldownAbusedEvent(npcEntityId));

        Assert.IsFalse(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            DrinkingProblemAchievementId));
    }
}
