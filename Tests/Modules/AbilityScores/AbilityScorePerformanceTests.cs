using System.Diagnostics;
using Engine.Bootstrap;
using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.Core;
using Game.Modules.Movement;
using Game.Modules.ProcessingTier;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.AbilityScores;

/// <summary>
/// Wall-clock benchmarks with a checked-in baseline comparison (AbilityScorePerformanceBaseline)
/// -- a new pattern for this repo (see that class's own doc comment for why), tagged
/// [TestCategory("Performance")] so it can be isolated from the rest of the suite in either
/// direction: `dotnet test Tests/Tests.csproj --filter "TestCategory=Performance"` runs only
/// these two, `--filter "TestCategory!=Performance"` skips them. A plain `dotnet test
/// Tests/Tests.csproj` (no filter) still runs them alongside everything else -- generous
/// tolerance keeps them non-flaky, and the added wall-clock cost is well under a second, so
/// there was no need to fight MSTest's runsettings-vs-filter precedence to exclude them by
/// default.
///
/// Exercises the two things this feature actually changes: NPC-population-time grant cost
/// (AbilityScoreEffects.GrantDefaults at FloorBuilder.PopulateFloor scale -- every race now
/// defaults to carrying 7 AbilityScoreComponents) and the event-driven expiry recompute path
/// (the hot path StatModifierExpiredEvent introduced, replacing the periodic-poll design this
/// module deliberately avoided -- see AbilityScoresModule's own doc comment for why a poll would
/// have been the wrong tradeoff at GameLoop.InitialEntityCapacity's ~2.6M-entity scale).
/// </summary>
[TestClass]
[TestCategory("Performance")]
public sealed class AbilityScorePerformanceTests
{
    private const int EntityCount = 100_000;

    [TestMethod]
    public void GrantDefaults_OneHundredThousandEntities_WithinBaselineTolerance()
    {
        var manager = new ComponentManager(initialEntityCapacity: EntityCount, initialComponentCapacity: EntityCount * 7);
        new StatModifiersModule().RegisterComponents(manager);
        new AbilityScoresModule().RegisterComponents(manager);

        var stopwatch = Stopwatch.StartNew();
        for (var entityId = 0; entityId < EntityCount; entityId++)
        {
            AbilityScoreEffects.GrantDefaults(manager, entityId, baseValue: 5);
        }
        stopwatch.Stop();

        AssertWithinBaseline(stopwatch.Elapsed.TotalMilliseconds, AbilityScorePerformanceBaseline.GrantDefaultsMilliseconds, nameof(AbilityScorePerformanceBaseline.GrantDefaultsMilliseconds));
    }

    [TestMethod]
    public void ExpiryTriggeredRecompute_OneHundredThousandEntities_WithinBaselineTolerance()
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
        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: EntityCount, initialComponentCapacity: EntityCount * 8);

        var entityIds = new int[EntityCount];
        for (var i = 0; i < EntityCount; i++)
        {
            var entityId = ecsContext.EntityManager.CreateEntity();
            entityIds[i] = entityId;
            AbilityScoreEffects.Grant(ecsContext.ComponentManager, entityId, AbilityScoreType.Strength, baseValue: 5);
        }

        foreach (var entityId in entityIds)
        {
            AbilityScoreEffects.GrantModifier(ecsContext.ComponentManager, entityId, AbilityScoreType.Strength, StatModifierOperation.Additive, StatModifierPolarity.Buff,
                canModify: true, magnitude: 3f, durationFrames: 1, StatusEffectSource.Admin);
        }

        // StatModifierExpirySystem has StripeCount 1 -- a single Update call ticks every
        // 1-frame modifier to 0, removes it, and publishes StatModifierExpiredEvent for each,
        // which AbilityScoresModule's subscription reacts to by recomputing Total -- this is
        // the actual hot path introduced by the event-driven design (see the class doc comment).
        var stopwatch = Stopwatch.StartNew();
        ecsContext.Update(default);
        stopwatch.Stop();

        AssertWithinBaseline(stopwatch.Elapsed.TotalMilliseconds, AbilityScorePerformanceBaseline.ExpiryRecomputeMilliseconds, nameof(AbilityScorePerformanceBaseline.ExpiryRecomputeMilliseconds));
    }

    private static void AssertWithinBaseline(double actualMilliseconds, double baselineMilliseconds, string baselineName)
    {
        var threshold = baselineMilliseconds * AbilityScorePerformanceBaseline.ToleranceMultiplier;
        Assert.IsLessThanOrEqualTo(
            threshold,
            actualMilliseconds,
            $"{baselineName}: took {actualMilliseconds:F1}ms, more than {AbilityScorePerformanceBaseline.ToleranceMultiplier}x the recorded baseline of {baselineMilliseconds:F1}ms. Either a real regression, or the baseline needs re-recording on this machine.");
    }
}
