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
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.AbilityScores;

/// <summary>
/// Wall-clock performance checks, tagged [TestCategory("Performance")] so they can be isolated
/// from the rest of the suite in either direction: `dotnet test Tests/Tests.csproj --filter
/// "TestCategory=Performance"` runs only these, `--filter "TestCategory!=Performance"` skips
/// them. A plain `dotnet test Tests/Tests.csproj` (no filter) still runs them alongside
/// everything else, in parallel with other test classes -- which is why both assert how cost
/// scales with entity count rather than an absolute time (see
/// GrantDefaults_ScalesLinearlyWithEntityCount's doc comment for the baseline-in-milliseconds
/// design this replaced, and why it flaked).
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

    /// <summary>The small size GrantDefaults' scaling is measured against -- EntityCount / 10, so linear cost gives a ratio of ~10.</summary>
    private const int ScalingSmallEntityCount = EntityCount / 10;

    /// <summary>
    /// Largest acceptable time(EntityCount) / time(ScalingSmallEntityCount). Linear is 10 and
    /// n log n about 12.5; quadratic is 100. 20 leaves room for the larger pools falling out of
    /// cache and for timer noise on the small run while still failing any per-call cost that
    /// grows with population.
    /// </summary>
    private const double MaxGrantDefaultsScalingRatio = 20;

    /// <summary>Largest acceptable expiry-recompute ratio -- same reasoning as MaxGrantDefaultsScalingRatio.</summary>
    private const double MaxExpiryRecomputeScalingRatio = 20;

    /// <summary>Each size is timed this many times, interleaved, and the fastest kept -- see MeasureGrantDefaults.</summary>
    private const int ScalingRepetitions = 3;

    /// <summary>
    /// Asserts how GrantDefaults' cost grows with population rather than what it costs on some
    /// machine. The wall-clock-baseline version this replaced (120ms recorded on the dev machine,
    /// 1.5x tolerance) passed alone at ~133ms but failed under the full parallel suite at
    /// 189-252ms: other test classes competing for cores inflated the absolute time, not the
    /// algorithm. A ratio of two timings taken back to back in the same process cancels out
    /// machine speed, build configuration and steady background load, and still catches what
    /// the test exists for -- an accidental O(n) per grant making population O(n^2).
    /// </summary>
    [TestMethod]
    public void GrantDefaults_ScalesLinearlyWithEntityCount()
    {
        // Warm-up so neither measured size pays JIT for GrantDefaults and the pool code.
        MeasureGrantDefaults(ScalingSmallEntityCount / 10);

        var smallMilliseconds = double.MaxValue;
        var largeMilliseconds = double.MaxValue;
        for (var repetition = 0; repetition < ScalingRepetitions; repetition++)
        {
            smallMilliseconds = System.Math.Min(smallMilliseconds, MeasureGrantDefaults(ScalingSmallEntityCount));
            largeMilliseconds = System.Math.Min(largeMilliseconds, MeasureGrantDefaults(EntityCount));
        }

        var ratio = largeMilliseconds / smallMilliseconds;
        Assert.IsLessThanOrEqualTo(
            MaxGrantDefaultsScalingRatio,
            ratio,
            $"GrantDefaults: {EntityCount:N0} entities took {largeMilliseconds:F1}ms vs {smallMilliseconds:F1}ms for {ScalingSmallEntityCount:N0} -- a {ratio:F1}x increase for 10x the entities (linear is ~10x). Per-grant cost is growing with population.");
    }

    /// <summary>
    /// Times GrantDefaults across entityCount entities on a fresh, pre-sized ComponentManager (so
    /// no pool growth lands inside the timing), after a full GC (so no collection of an earlier
    /// run's garbage does). Callers take the minimum across interleaved repetitions: interference
    /// from parallel tests only ever adds time, so the fastest run is the closest to the real
    /// cost, and interleaving keeps a burst of interference from landing on one size only.
    /// </summary>
    private static double MeasureGrantDefaults(int entityCount)
    {
        var manager = new ComponentManager(initialEntityCapacity: entityCount, initialComponentCapacity: entityCount * 7);
        new StatModifiersModule().RegisterComponents(manager);
        new AbilityScoresModule().RegisterComponents(manager);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var stopwatch = Stopwatch.StartNew();
        for (var entityId = 0; entityId < entityCount; entityId++)
        {
            AbilityScoreEffects.GrantDefaults(manager, entityId, baseValue: 5);
        }
        stopwatch.Stop();

        return stopwatch.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// Same shape as GrantDefaults_ScalesLinearlyWithEntityCount, for the event-driven expiry
    /// recompute path: one StatModifierExpirySystem tick expiring every entity's modifier, each
    /// expiry publishing StatModifierExpiredEvent and AbilityScoresModule recomputing that
    /// entity's Total. Linear means each expiry's cost is independent of how many other entities
    /// exist; the regression this guards against is that path turning into a scan (the
    /// per-frame poll the event-driven design replaced -- see the class doc comment).
    /// </summary>
    [TestMethod]
    public void ExpiryTriggeredRecompute_ScalesLinearlyWithEntityCount()
    {
        MeasureExpiryRecompute(ScalingSmallEntityCount / 10);

        var smallMilliseconds = double.MaxValue;
        var largeMilliseconds = double.MaxValue;
        for (var repetition = 0; repetition < ScalingRepetitions; repetition++)
        {
            smallMilliseconds = System.Math.Min(smallMilliseconds, MeasureExpiryRecompute(ScalingSmallEntityCount));
            largeMilliseconds = System.Math.Min(largeMilliseconds, MeasureExpiryRecompute(EntityCount));
        }

        var ratio = largeMilliseconds / smallMilliseconds;
        Assert.IsLessThanOrEqualTo(
            MaxExpiryRecomputeScalingRatio,
            ratio,
            $"Expiry recompute: {EntityCount:N0} entities took {largeMilliseconds:F1}ms vs {smallMilliseconds:F1}ms for {ScalingSmallEntityCount:N0} -- a {ratio:F1}x increase for 10x the entities (linear is ~10x). Per-expiry cost is growing with population.");
    }

    /// <summary>
    /// Builds a context with entityCount entities, each holding one Strength score and one
    /// 1-frame modifier on it, then times the single EcsContext.Update that expires them all.
    /// Setup is outside the timing; see MeasureGrantDefaults for the GC/minimum rationale.
    /// </summary>
    private static double MeasureExpiryRecompute(int entityCount)
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
        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: entityCount, initialComponentCapacity: entityCount * 8);
        var processingTiers = ecsContext.ComponentManager.GetDirectPool<ProcessingTierComponent>();

        var entityIds = new int[entityCount];
        for (var i = 0; i < entityCount; i++)
        {
            var entityId = ecsContext.EntityManager.CreateEntity();
            entityIds[i] = entityId;
            // Tier no longer affects expiry (StatModifierExpirySystem is on the timer wheel, so
            // every modifier expires on its exact frame regardless), but the entity still has to be
            // tiered for the other systems in this fixture.
            processingTiers.Add(entityId, new ProcessingTierComponent(ProcessingTierLevel.Local));
            AbilityScoreEffects.Grant(ecsContext.ComponentManager, entityId, AbilityScoreType.Strength, baseValue: 5);
        }

        foreach (var entityId in entityIds)
        {
            AbilityScoreEffects.GrantModifier(ecsContext.ComponentManager, entityId, AbilityScoreType.Strength, StatModifierOperation.Additive, StatModifierPolarity.Buff,
                canModify: true, magnitude: 3f, expiresAtFrame: 0, StatusEffectSource.Admin);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Every modifier's deadline is frame 0, so this single Update fires all of them at once,
        // removes them, and publishes
        // StatModifierExpiredEvent for each, which AbilityScoresModule's subscription reacts to by
        // recomputing Total -- the hot path the event-driven design introduced.
        var stopwatch = Stopwatch.StartNew();
        ecsContext.Update(default);
        stopwatch.Stop();

        // Guards the measurement itself: if any modifier survived, the tick timed less work than
        // it claims to, and the ratio would compare two different fractions of the population.
        Assert.AreEqual(0, ecsContext.ComponentManager.GetPackedPool<ExpiringStatModifierComponent>().Count, "Not every modifier expired in the measured tick.");

        return stopwatch.Elapsed.TotalMilliseconds;
    }
}
