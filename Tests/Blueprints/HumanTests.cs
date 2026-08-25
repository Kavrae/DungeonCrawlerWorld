using Engine.Bootstrap;
using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Blueprints.Races;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Class;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Crawler;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Mana;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.Race;
using Game.Modules.Race.Components;
using Game.Modules.StatModifiers;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Blueprints;

[TestClass]
public sealed class HumanTests
{
    private static EcsContext BuildEcsContext()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var mathUtility = new MathUtility();
        var context = new GameModuleContext(world, mathUtility, new EventBus()) { EntityMoveSync = new WorldEventSync(world) };

        var movementModule = new MovementModule();
        movementModule.Configure(context);

        var actionsModule = new ActionsModule();
        actionsModule.Configure(context);

        var coreActionsModule = new CoreActionsModule();
        coreActionsModule.Configure(context);

        var processingTierModule = new ProcessingTierModule();
        processingTierModule.Configure(context);

        var coreModule = new CoreModule();
        coreModule.Configure(context);

        var healthModule = new HealthModule();
        healthModule.Configure(context);

        var manaModule = new ManaModule();
        manaModule.Configure(context);

        var statModifiersModule = new StatModifiersModule();
        statModifiersModule.Configure(context);

        var abilityScoresModule = new AbilityScoresModule();
        abilityScoresModule.Configure(context);

        var coreItemsModule = new CoreItemsModule();
        coreItemsModule.Configure(context);

        IReadOnlyList<IModule> modules =
        [
            coreModule,
            healthModule,
            manaModule,
            statModifiersModule,
            abilityScoresModule,
            movementModule,
            new RaceModule(),
            new ClassModule(),
            actionsModule,
            coreActionsModule,
            new CrawlerModule(),
            processingTierModule,
            new InventoryModule(),
            coreItemsModule,
        ];

        return Bootstrapper.Build(modules, initialEntityCapacity: 100, initialComponentCapacity: 50);
    }

    [TestMethod]
    public void Build_GrantsRaceGlyphBodyPartsMovementActionLockTransformAbilityScoresAndPunch()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        new Human(new MathUtility(new Random(1))).Build(ecsContext.ComponentManager, entityId);

        var racePool = ecsContext.ComponentManager.GetMultiPool<RaceComponent>();
        Assert.IsTrue(racePool.Has(entityId));
        Assert.AreEqual(Human.RaceId, racePool.GetReadonlyByDenseIndex(racePool.GetFirstDenseIndex(entityId)).Id);
        Assert.AreEqual("Human", racePool.GetReadonlyByDenseIndex(racePool.GetFirstDenseIndex(entityId)).Name);

        Assert.IsFalse(ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().Has(entityId));

        var glyph = ecsContext.ComponentManager.GetDirectPool<GlyphComponent>().GetReadonly(entityId);
        Assert.AreEqual("h", glyph.Glyph);
        Assert.AreEqual(Color.Pink, glyph.GlyphColor);

        var movement = ecsContext.ComponentManager.GetPackedPool<MovementComponent>().GetReadonly(entityId);
        Assert.AreEqual(MovementMode.Random, movement.MovementMode);

        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));

        foreach (var abilityScoreType in Enum.GetValues<AbilityScoreType>())
        {
            Assert.IsTrue(AbilityScoreQueries.TryGetComponent(ecsContext.ComponentManager.GetMultiPool<AbilityScoreComponent>(), entityId, abilityScoreType, out _), $"Missing ability score: {abilityScoreType}");
        }

        Assert.IsTrue(ActionInstanceQueries.TryGet(ecsContext.ComponentManager.GetMultiPool<ActionInstanceComponent>(), entityId, PunchAction.Id, out _));

        var expectedPartsByName = new Dictionary<string, (BodyPartType Type, ushort MinimumHealth, ushort MaximumHealth, bool IsVital)>
        {
            ["Head"] = (BodyPartType.Head, 40, 40, true),
            ["Torso"] = (BodyPartType.Torso, 80, 80, true),
            ["Left Arm"] = (BodyPartType.Arm, 25, 25, false),
            ["Right Arm"] = (BodyPartType.Arm, 25, 25, false),
            ["Left Leg"] = (BodyPartType.Leg, 40, 40, false),
            ["Right Leg"] = (BodyPartType.Leg, 40, 40, false),
        };

        var bodyParts = ecsContext.ComponentManager.GetMultiPool<BodyPartComponent>();
        var actualCount = 0;
        var actualMaximumSum = 0f;
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref bodyParts.GetReadonlyByDenseIndex(denseIndex);
            Assert.IsTrue(expectedPartsByName.TryGetValue(part.Name, out var expected), $"Unexpected body part name: {part.Name}");
            Assert.AreEqual(expected.Type, part.Type);
            Assert.AreEqual(expected.IsVital, part.IsVital);
            Assert.AreEqual((float)expected.MaximumHealth, part.MaximumHealth);
            // Min == Max for every part here, so current health always equals maximum.
            Assert.AreEqual((float)expected.MaximumHealth, part.CurrentHealth);
            actualMaximumSum += part.MaximumHealth;
            actualCount++;
        }

        Assert.AreEqual(expectedPartsByName.Count, actualCount);
        Assert.AreEqual(250f, actualMaximumSum);
    }
}
