using Engine.ECS.Systems;
using Engine.Math;
using Game.Bootstrap;
using Game.World;
using Game.Modules;

namespace Tests.Bootstrap;

/// <summary>The built-in module set's identities and the order its systems run in.</summary>
[TestClass]
public sealed class BuiltInModulesTests
{
    private static readonly DirectoryInfo EmptyModsDirectory = Directory.CreateTempSubdirectory();

    [ClassCleanup]
    public static void DeleteEmptyModsDirectory() => EmptyModsDirectory.Delete(recursive: true);

    [TestMethod]
    public void EveryBuiltInModule_HasANonEmptyId()
    {
        foreach (var module in GameBootstrapper.BuiltInModules())
        {
            Assert.AreNotEqual(Guid.Empty, module.Id, module.Name);
        }
    }

    [TestMethod]
    public void NoTwoBuiltInModules_ShareAnId()
    {
        var duplicateIds = GameBootstrapper.BuiltInModules()
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

        foreach (var module in GameBootstrapper.BuiltInModules())
        {
            var closure = RequiresClosure(module, GameBootstrapper.BuiltInModules());
            var world = new Game.World.World(new Map(new Vector3Int(10, 10, 3)));
            var context = new GameModuleContext(world, new MathUtility(new Random(1)), new Engine.Events.EventBus()) { PlayerQuery = world, EntityMoveSync = new WorldEventSync(world) };

            try
            {
                foreach (var gameModule in closure.OfType<IGameModule>())
                {
                    gameModule.Configure(context);
                }

                Engine.Bootstrap.Bootstrapper.Build(closure, initialEntityCapacity: 10, initialComponentCapacity: 10, context.EventBus, entityKeys: context.EntityKeys);
            }
            catch (Exception exception)
            {
                failures.Add($"{module.Name} (with {string.Join(", ", closure.Select(member => member.Name))}): {exception.Message}");
            }
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    private static List<Engine.Modules.IModule> RequiresClosure(Engine.Modules.IModule module, IReadOnlyList<Engine.Modules.IModule> builtInModules)
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
        var world = new Game.World.World(new Map(new Vector3Int(40, 40, 3)));
        var result = GameBootstrapper.Build(world, new MathUtility(new Random(1)), EmptyModsDirectory.FullName, initialEntityCapacity: 100, initialComponentCapacity: 50);

        var testComponents = BuiltInTestComponents.RegisterAll(new Engine.ECS.Components.ComponentManager(100, 50));

        CollectionAssert.AreEquivalent(
            result.EcsContext.ComponentManager.AllPools.Select(pool => pool.GetType()).ToList(),
            testComponents.AllPools.Select(pool => pool.GetType()).ToList());
    }

    [TestMethod]
    public void BuiltInSystems_RunInThePinnedOrder()
    {
        var world = new Game.World.World(new Map(new Vector3Int(40, 40, 3)));
        var result = GameBootstrapper.Build(world, new MathUtility(new Random(1)), EmptyModsDirectory.FullName, initialEntityCapacity: 100, initialComponentCapacity: 50);
        var recorder = new SystemOrderRecorder();
        result.EcsContext.SystemManager.Profiler = recorder;

        result.EcsContext.SystemManager.Update(new EngineTime(TimeSpan.Zero, TimeSpan.FromSeconds(1d / 60), false, 1));

        string[] expectedSystemOrder =
        [
            "SpawnMoves",
            "SimpleHealthRegenSystem",
            "ComplexHealthRegenSystem",
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
            "StatusEffectImmunityExpirySystem",
            "StatModifierExpirySystem",
            "BodyPartEffectsSystem",
            "BurningSystem",
            "BodyPartBurningSystem",
            "PoisonSystem",
            "ParalysisSystem",
            "ContactDamageSystem",
            "StatusEffectAuraSystem",
            "AuraSourceExpirySystem",
            "AchievementPollingSystem",
            "ConsumableActivationSystem",
            "ContainerDestructionSystem",
        ];

        CollectionAssert.AreEqual(expectedSystemOrder, recorder.SystemNames, string.Join(Environment.NewLine, recorder.SystemNames));
    }
}
