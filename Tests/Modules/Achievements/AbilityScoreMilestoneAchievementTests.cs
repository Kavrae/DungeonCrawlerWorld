using Engine.Bootstrap;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.Achievements;
using Game.Modules.Achievements.Components;
using Game.Modules.Achievements.Definitions;
using Game.Modules.Core;
using Game.Modules.Movement;
using Game.Modules.ProcessingTier;
using Game.Modules.StatModifiers;
using Game.World;

namespace Tests.Modules.Achievements;

/// <summary>
/// Exercises the base-ability-score milestone achievements (BigMuscles, Unbreakable, ShanghaiKid,
/// RevengeOfTheNerds, KillerQueen, MinMaxer) end-to-end through the real AchievementModule,
/// mirroring AchievementModuleTests' own Build pattern. AbilityScoresModule declares a dependency
/// on StatModifiersModule, whose own RegisterSystems needs ProcessingTierComponent
/// (ProcessingTierModule), whose own RegisterSystems needs TransformComponent/MovementComponent
/// (CoreModule/MovementModule) -- so the full chain has to be built, same reasoning as
/// InertGasAchievementTests' own Build pattern (see its doc comment).
/// </summary>
[TestClass]
public sealed class AbilityScoreMilestoneAchievementTests
{
    private static readonly Guid BigMusclesAchievementId = new BigMusclesAchievement().Id;

    private static readonly Guid UnbreakableAchievementId = new UnbreakableAchievement().Id;

    private static readonly Guid ShanghaiKidAchievementId = new ShanghaiKidAchievement().Id;

    private static readonly Guid RevengeOfTheNerdsAchievementId = new RevengeOfTheNerdsAchievement().Id;

    private static readonly Guid KillerQueenAchievementId = new KillerQueenAchievement().Id;

    private static readonly Guid MinMaxerAchievementId = new MinMaxerAchievement().Id;

    private static (EcsContext EcsContext, EventBus EventBus, Game.World.World World) Build()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var eventBus = new EventBus();
        var context = new GameModuleContext(world, new MathUtility(), eventBus) { PlayerQuery = world, EntityMoveSync = new WorldEventSync(world) };

        var coreModule = new CoreModule();
        coreModule.Configure(context);

        var movementModule = new MovementModule();
        movementModule.Configure(context);

        var processingTierModule = new ProcessingTierModule();
        processingTierModule.Configure(context);

        var statModifiersModule = new StatModifiersModule();
        statModifiersModule.Configure(context);

        var abilityScoresModule = new AbilityScoresModule();
        abilityScoresModule.Configure(context);

        var module = new AchievementModule();
        module.Configure(context);

        IReadOnlyList<IModule> modules = [module, coreModule, movementModule, processingTierModule, statModifiersModule, abilityScoresModule];
        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: 10, initialComponentCapacity: 10, eventBus);

        return (ecsContext, eventBus, world);
    }

    private static bool HasEarned(EcsContext ecsContext, int entityId, Guid achievementId) =>
        AchievementQueries.HasEarned(ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(), entityId, achievementId);

    [TestMethod]
    public void PlayerBaseStrengthReaches100_UnlocksBigMuscles()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        AbilityScoreEffects.Grant(ecsContext.ComponentManager, playerEntityId, AbilityScoreType.Strength, 5);

        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, playerEntityId, AbilityScoreType.Strength, 100);

        Assert.IsTrue(HasEarned(ecsContext, playerEntityId, BigMusclesAchievementId));
    }

    [TestMethod]
    public void PlayerBaseStrengthBelow100_DoesNotUnlockBigMuscles()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        AbilityScoreEffects.Grant(ecsContext.ComponentManager, playerEntityId, AbilityScoreType.Strength, 5);

        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, playerEntityId, AbilityScoreType.Strength, 99);

        Assert.IsFalse(HasEarned(ecsContext, playerEntityId, BigMusclesAchievementId));
    }

    [TestMethod]
    public void PlayerBaseConstitutionReaches100_DoesNotUnlockBigMuscles()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        AbilityScoreEffects.Grant(ecsContext.ComponentManager, playerEntityId, AbilityScoreType.Constitution, 5);

        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, playerEntityId, AbilityScoreType.Constitution, 100);

        Assert.IsFalse(HasEarned(ecsContext, playerEntityId, BigMusclesAchievementId));
    }

    [TestMethod]
    public void NonPlayerBaseStrengthReaches100_DoesNotUnlockBigMusclesForPlayer()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        var npcEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        AbilityScoreEffects.Grant(ecsContext.ComponentManager, npcEntityId, AbilityScoreType.Strength, 5);

        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, npcEntityId, AbilityScoreType.Strength, 100);

        Assert.IsFalse(HasEarned(ecsContext, playerEntityId, BigMusclesAchievementId));
    }

    [TestMethod]
    public void PlayerBaseConstitutionReaches100_UnlocksUnbreakable()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        AbilityScoreEffects.Grant(ecsContext.ComponentManager, playerEntityId, AbilityScoreType.Constitution, 5);

        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, playerEntityId, AbilityScoreType.Constitution, 100);

        Assert.IsTrue(HasEarned(ecsContext, playerEntityId, UnbreakableAchievementId));
    }

    [TestMethod]
    public void PlayerBaseDexterityReaches100_UnlocksShanghaiKid()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        AbilityScoreEffects.Grant(ecsContext.ComponentManager, playerEntityId, AbilityScoreType.Dexterity, 5);

        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, playerEntityId, AbilityScoreType.Dexterity, 100);

        Assert.IsTrue(HasEarned(ecsContext, playerEntityId, ShanghaiKidAchievementId));
    }

    [TestMethod]
    public void PlayerBaseIntelligenceReaches100_UnlocksRevengeOfTheNerds()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        AbilityScoreEffects.Grant(ecsContext.ComponentManager, playerEntityId, AbilityScoreType.Intelligence, 5);

        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, playerEntityId, AbilityScoreType.Intelligence, 100);

        Assert.IsTrue(HasEarned(ecsContext, playerEntityId, RevengeOfTheNerdsAchievementId));
    }

    [TestMethod]
    public void PlayerBaseCharismaReaches100_UnlocksKillerQueen()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        AbilityScoreEffects.Grant(ecsContext.ComponentManager, playerEntityId, AbilityScoreType.Charisma, 5);

        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, playerEntityId, AbilityScoreType.Charisma, 100);

        Assert.IsTrue(HasEarned(ecsContext, playerEntityId, KillerQueenAchievementId));
    }

    private static void GrantAllCoreScores(EcsContext ecsContext, int entityId)
    {
        AbilityScoreEffects.Grant(ecsContext.ComponentManager, entityId, AbilityScoreType.Strength, 5);
        AbilityScoreEffects.Grant(ecsContext.ComponentManager, entityId, AbilityScoreType.Constitution, 5);
        AbilityScoreEffects.Grant(ecsContext.ComponentManager, entityId, AbilityScoreType.Dexterity, 5);
        AbilityScoreEffects.Grant(ecsContext.ComponentManager, entityId, AbilityScoreType.Intelligence, 5);
        AbilityScoreEffects.Grant(ecsContext.ComponentManager, entityId, AbilityScoreType.Charisma, 5);
    }

    [TestMethod]
    public void AllFiveCoreScoresReach300_UnlocksMinMaxer()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        GrantAllCoreScores(ecsContext, playerEntityId);

        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, playerEntityId, AbilityScoreType.Strength, 300);
        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, playerEntityId, AbilityScoreType.Constitution, 300);
        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, playerEntityId, AbilityScoreType.Dexterity, 300);
        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, playerEntityId, AbilityScoreType.Intelligence, 300);
        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, playerEntityId, AbilityScoreType.Charisma, 300);

        Assert.IsTrue(HasEarned(ecsContext, playerEntityId, MinMaxerAchievementId));
    }

    [TestMethod]
    public void OnlyFourOfFiveCoreScoresReach300_DoesNotUnlockMinMaxer()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        GrantAllCoreScores(ecsContext, playerEntityId);

        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, playerEntityId, AbilityScoreType.Strength, 300);
        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, playerEntityId, AbilityScoreType.Constitution, 300);
        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, playerEntityId, AbilityScoreType.Dexterity, 300);
        AbilityScoreEffects.SetBaseValue(ecsContext.ComponentManager, eventBus, playerEntityId, AbilityScoreType.Intelligence, 300);

        Assert.IsFalse(HasEarned(ecsContext, playerEntityId, MinMaxerAchievementId));
    }
}
