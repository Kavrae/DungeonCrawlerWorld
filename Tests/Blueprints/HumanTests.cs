using Engine.ECS.Entities;
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
using Game.Blueprints;

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
            new Game.Blueprints.BlueprintsModule(),
            new ClassModule(),
            actionsModule,
            coreActionsModule,
            new CrawlerModule(),
            processingTierModule,
            new InventoryModule(),
            coreItemsModule,
        ];

        return Bootstrapper.Build(modules, initialEntityCapacity: 100, initialComponentCapacity: 50, entityKeys: context.EntityKeys);
    }

    [TestMethod]
    public void Build_GrantsRaceGlyphBodyPartsMovementActionLockTransformAbilityScoresAndQuickAttack()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();

        ecsContext.BuildDefinition(entityId, Human.Id);

        var racePool = ecsContext.ComponentManager.GetPackedPool<RaceSlotsComponent>();
        Assert.IsTrue(racePool.Has(entityId));
        Assert.AreEqual(BlueprintTestContext.Definitions.Races.GetId(Human.Id), racePool.GetReadonly(entityId).Primary);

        Assert.IsFalse(ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().Has(entityId));

        // A creature draws as its blueprint rather than holding a glyph of its own -- see EntityAppearance.
        Assert.IsFalse(ecsContext.ComponentManager.GetPackedPool<GlyphComponent>().Has(entityId));
        Assert.AreEqual("h", Human.Appearance.Glyph);
        Assert.AreEqual(Color.Pink, Human.Appearance.GlyphColor);

        var movement = ecsContext.ComponentManager.GetPackedPool<MovementComponent>().GetReadonly(entityId);
        Assert.AreEqual(MovementMode.Random, movement.MovementMode);

        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Has(entityId));
        Assert.IsTrue(ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Has(entityId));

        foreach (var abilityScoreType in Enum.GetValues<AbilityScoreType>())
        {
            Assert.IsTrue(AbilityScoreQueries.TryGetComponent(ecsContext.ComponentManager.GetPackedPool<AbilityScoresComponent>(), entityId, abilityScoreType, out _), $"Missing ability score: {abilityScoreType}");
        }

        Assert.IsTrue(ecsContext.ActionsOf().Has(entityId, QuickAttackAction.Id));

        var expectedPartsByName = new Dictionary<string, (BodyPartType Type, ushort MinimumHealth, ushort MaximumHealth, bool IsVital)>
        {
            ["Head"] = (BodyPartType.Head, 40, 40, true),
            ["Torso"] = (BodyPartType.Torso, 65, 65, true),
            ["Internal"] = (BodyPartType.Internal, 15, 15, true),
            ["Left Arm"] = (BodyPartType.Arm, 20, 20, false),
            ["Right Arm"] = (BodyPartType.Arm, 20, 20, false),
            ["Left Hand"] = (BodyPartType.Hand, 5, 5, false),
            ["Right Hand"] = (BodyPartType.Hand, 5, 5, false),
            ["Left Leg"] = (BodyPartType.Leg, 30, 30, false),
            ["Right Leg"] = (BodyPartType.Leg, 30, 30, false),
            ["Left Foot"] = (BodyPartType.Foot, 10, 10, false),
            ["Right Foot"] = (BodyPartType.Foot, 10, 10, false),
        };

        var bodyParts = EntityBodyParts.For(ecsContext.ComponentManager, BlueprintTestContext.Definitions);
        var actualCount = 0;
        var actualMaximumSum = 0f;
        foreach (var part in bodyParts.Parts(entityId))
        {
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
