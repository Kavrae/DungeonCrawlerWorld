using Engine.Diagnostics;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Modules;
using Game.Bootstrap;
using Game.World;
using Game.Modules;

namespace Tests.Bootstrap;

/// <summary>The built-in module set's identities and the order its systems run in.</summary>
[TestClass]
public sealed class BuiltInModulesTests
{
    [TestMethod]
    public void EveryBuiltInModule_HasANonEmptyId()
    {
        foreach (var module in GameBootstrapper.BuiltInModules().CreateAll())
        {
            Assert.AreNotEqual(Guid.Empty, module.Id, module.Name);
        }
    }

    [TestMethod]
    public void NoTwoBuiltInModules_ShareAnId()
    {
        var duplicateIds = GameBootstrapper.BuiltInModules().CreateAll()
            .GroupBy(module => module.Id)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(module => module.Name)))
            .ToList();

        Assert.IsEmpty(duplicateIds, string.Join("; ", duplicateIds));
    }

    [TestMethod]
    public void EveryBuiltInModule_BuildsWithOnlyWhatItRequires()
    {
        var failures = new List<string>();

        foreach (var module in GameBootstrapper.BuiltInModules().CreateAll())
        {
            var closure = RequiresClosure(module, GameBootstrapper.BuiltInModules().CreateAll());
            try
            {
                BuiltInTestModules.BuildModules(closure, new Map(new Vector3Int(10, 10, 3)), new MathUtility(new Random(1)));
            }
            catch (Exception exception)
            {
                failures.Add($"{module.Name} (with {string.Join(", ", closure.Select(member => member.Name))}): {exception.Message}");
            }
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    private static List<Engine.Modules.IModule<GameModuleContext>> RequiresClosure(Engine.Modules.IModule module, IReadOnlyList<Engine.Modules.IModule<GameModuleContext>> builtInModules)
    {
        var modulesById = builtInModules.ToDictionary(builtIn => builtIn.Id);
        var closureIds = new HashSet<Guid>();
        var pendingIds = new Stack<Guid>([module.Id]);

        while (pendingIds.TryPop(out var id))
        {
            if (closureIds.Add(id))
            {
                foreach (var requiredId in modulesById[id].Requires)
                {
                    pendingIds.Push(requiredId);
                }
            }
        }

        return builtInModules.Where(builtIn => closureIds.Contains(builtIn.Id)).ToList();
    }

    [TestMethod]
    public void BuiltInTestComponents_RegistersTheSamePoolsAsTheGame()
    {
        var map = new Map(new Vector3Int(40, 40, 3));
        var result = GameBootstrapper.Build(ValidatedMods.None, map, new MathUtility(new Random(1)), initialEntityCapacity: 100, initialComponentCapacity: 50);

        var testComponents = BuiltInTestComponents.RegisterAll(new Engine.ECS.Components.ComponentManager(100, 50));

        CollectionAssert.AreEquivalent(
            result.EcsContext.ComponentManager.AllPools.Select(pool => pool.GetType()).ToList(),
            testComponents.AllPools.Select(pool => pool.GetType()).ToList());
    }

    [TestMethod]
    [DoNotParallelize]
    public void BuiltInSystems_RunInThePinnedOrder()
    {
        var map = new Map(new Vector3Int(40, 40, 3));
        var result = GameBootstrapper.Build(ValidatedMods.None, map, new MathUtility(new Random(1)), initialEntityCapacity: 100, initialComponentCapacity: 50);
        var recorder = new SystemOrderRecorder();
        using var frameCostSubscription = EngineHooks.FrameCosts.Subscribe(recorder);

        result.EcsContext.SystemManager.Update(new EngineTime(TimeSpan.Zero, TimeSpan.FromSeconds(1d / 60), false, 1));

        string[] expectedSystemOrder =
        [
            "SpawnMoves",
            "SimpleHealthRegenSystem",
            "ComplexHealthRegenSystem",
            "DamageLedgerExpirySystem",
            "ManaRegenSystem",
            "TestCombatBehaviorSystem",
            "TestDummyAttackSystem",
            "MovementSystem",
            "DeathSystem",
            "ProcessingTierSystem",
            "PotionCooldownSystem",
            "DodgeExpirySystem",
            "DelayedActionSystem",
            "ActionActivationSystem",
            "ToggleUpkeepSystem",
            "StatusEffectImmunityExpirySystem",
            "StatModifierExpirySystem",
            "BodyPartEffectsSystem",
            "BurningSystem",
            "BodyPartBurningSystem",
            "PoisonSystem",
            "ParalysisSystem",
            "TerrainContactSystem",
            "AuraSystem",
            "AuraSourceExpirySystem",
            "AuraAnchorEndingSystem",
            "AchievementPollingSystem",
            "ItemActivationSystem",
            "ContainerDestructionSystem",
        ];

        CollectionAssert.AreEqual(expectedSystemOrder, recorder.SystemNames, string.Join(Environment.NewLine, recorder.SystemNames));
    }
}
