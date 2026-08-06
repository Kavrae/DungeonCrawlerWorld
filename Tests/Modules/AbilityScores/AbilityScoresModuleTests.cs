using Engine.Bootstrap;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Core;
using Game.Modules.Movement;
using Game.Modules.ProcessingTier;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.AbilityScores;

/// <summary>
/// End-to-end coverage through the real Bootstrapper -- AbilityScoreEffectsTests already covers
/// AbilityScoreEffects/AbilityScoreMath directly against a hand-built ComponentManager; this
/// class instead proves the module wiring itself (Dependencies enforcement, and the
/// StatModifierExpiredEvent subscription actually firing end-to-end through real systems).
/// </summary>
[TestClass]
public sealed class AbilityScoresModuleTests
{
    private static (EcsContext EcsContext, int EntityId) BuildAndGrantStrength(short baseValue)
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var mathUtility = new MathUtility();
        var context = new GameModuleContext(world, mathUtility, new EventBus()) { EntityMoveSync = new WorldEventSync(world) };

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

        IReadOnlyList<IModule> modules = [coreModule, movementModule, processingTierModule, statModifiersModule, abilityScoresModule];

        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: 10, initialComponentCapacity: 10);

        var entityId = ecsContext.EntityManager.CreateEntity();
        AbilityScoreEffects.Grant(ecsContext.ComponentManager, entityId, AbilityScoreType.Strength, baseValue);

        return (ecsContext, entityId);
    }

    private static AbilityScoreComponent GetStrength(EcsContext ecsContext, int entityId)
    {
        var pool = ecsContext.ComponentManager.GetMultiPool<AbilityScoreComponent>();
        for (var denseIndex = pool.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            var component = pool.GetReadonlyByDenseIndex(denseIndex);
            if (component.Type == AbilityScoreType.Strength)
            {
                return component;
            }
        }

        throw new InvalidOperationException("No Strength AbilityScoreComponent found.");
    }

    [TestMethod]
    public void Build_MissingStatModifiersModule_ThrowsInvalidOperationException()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var mathUtility = new MathUtility();
        var context = new GameModuleContext(world, mathUtility, new EventBus());

        var processingTierModule = new ProcessingTierModule();
        processingTierModule.Configure(context);

        var abilityScoresModule = new AbilityScoresModule();
        abilityScoresModule.Configure(context);

        IReadOnlyList<IModule> modules = [processingTierModule, abilityScoresModule];

        Assert.ThrowsExactly<InvalidOperationException>(() => Bootstrapper.Build(modules, initialEntityCapacity: 10, initialComponentCapacity: 10));
    }

    [TestMethod]
    public void Build_WithStatModifiersModule_RegistersAbilityScoreComponentPool()
    {
        var (ecsContext, _) = BuildAndGrantStrength(baseValue: 5);

        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<AbilityScoreComponent>());
    }

    [TestMethod]
    public void TemporaryModifierExpiring_UpdatesTotalThroughRealEventSubscription()
    {
        var (ecsContext, entityId) = BuildAndGrantStrength(baseValue: 5);

        AbilityScoreEffects.GrantModifier(ecsContext.ComponentManager, entityId, AbilityScoreType.Strength, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: 3f, durationFrames: 1, StatusEffectSource.Admin);
        Assert.AreEqual((short)8, GetStrength(ecsContext, entityId).Total);

        // StatModifierExpirySystem has StripeCount 1 (visits every entity every real frame), so
        // one Update call ticks the 1-frame modifier to 0 and removes it, publishing
        // StatModifierExpiredEvent -- which AbilityScoresModule's subscription should react to
        // by recomputing Total back down.
        ecsContext.Update(default);

        Assert.AreEqual((short)5, GetStrength(ecsContext, entityId).Total);
    }
}
