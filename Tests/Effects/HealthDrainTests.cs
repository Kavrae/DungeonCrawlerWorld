using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Spawning;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Effects;

/// <summary>Health as a cost: refused rather than killing its payer, split evenly across a body plan, never reduced by damage reduction, never rounded.</summary>
[TestClass]
public sealed class HealthDrainTests
{
    private const int PayerEntityId = 1;
    private const int Ground = (int)MapLayer.Ground;

    private static (EffectContext Context, ComponentManager Components) SimplePayer(float currentHealth)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(16, 16));
        var health = componentManager.GetPackedPool<SimpleHealthComponent>();
        health.Add(PayerEntityId, new SimpleHealthComponent(currentHealth, 100));
        componentManager.GetPackedPool<ManaComponent>().Add(PayerEntityId, new ManaComponent(100, 100));
        var context = TestActionEffects.Context(PayerEntityId, PayerEntityId, health, new EventBus(), new MathUtility(new Random(1)), componentManager, new EntityKeys(), "Test", default, Now: 0,
            StatModifiers: componentManager.GetMultiPool<StatModifierComponent>(), Mana: componentManager.GetPackedPool<ManaComponent>());
        return (context, componentManager);
    }

    private static float HealthOf(ComponentManager components) => components.GetPackedPool<SimpleHealthComponent>().GetReadonly(PayerEntityId).CurrentHealth;

    [TestMethod]
    public void TakesItsAmount_AndDamageReductionDoesNotApply()
    {
        var (context, components) = SimplePayer(currentHealth: 10);
        StatModifierEffects.Apply(components, PayerEntityId, StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: true, magnitude: -0.9f, FrameDeadline.Never, ActionSource.Admin);

        new HealthDrain(3).Apply(in context);

        Assert.AreEqual(7f, HealthOf(components), 0.001f);
    }

    [TestMethod]
    public void HalvedByItsOwnModifier_TakesTheFraction()
    {
        var (context, components) = SimplePayer(currentHealth: 10);
        StatModifierEffects.Apply(components, PayerEntityId, StatModifierTarget.IncomingHealthDrain, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: true, magnitude: -0.5f, FrameDeadline.Never, ActionSource.Admin);

        new HealthDrain(1).Apply(in context);

        Assert.AreEqual(9.5f, HealthOf(components), 0.001f);
    }

    [TestMethod]
    public void ACostThatWouldKill_IsRefused_AndOneThatLeavesAnyHealth_IsNot()
    {
        IReadOnlyList<Effect> costsThree = [new Effect([new HealthDrain(3)])];

        Assert.AreEqual(EffectRefusal.NotEnoughHealth, EffectSequence.CanApply(costsThree, SimplePayer(currentHealth: 3).Context));
        Assert.AreEqual(EffectRefusal.None, EffectSequence.CanApply(costsThree, SimplePayer(currentHealth: 3.1f).Context));
    }

    [TestMethod]
    public void TwoHealthCostsInOneList_AreCheckedTogether()
    {
        var (context, _) = SimplePayer(currentHealth: 5);

        Assert.AreEqual(EffectRefusal.NotEnoughHealth, EffectSequence.CanApply([new Effect([new HealthDrain(3), new HealthDrain(3)])], in context));
    }

    [TestMethod]
    public void APayerWithNoHealth_IsRefusedAsHavingNone()
    {
        var (context, _) = SimplePayer(currentHealth: 10);

        Assert.AreEqual(EffectRefusal.NoHealth, EffectSequence.CanApply([new Effect([new HealthDrain(1)])], context with { TargetEntityId = 9 }));
    }

    [TestMethod]
    public void ABodyPlan_PaysAnEvenShareFromEveryPart_AndIsRefusedWhenAVitalPartsShareWouldKill()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(20, 20, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 100, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(new Vector3Int(5, 5, Ground));
        var goblinId = build.Factory.Spawn(SpawnRequest.At(build.Context.Definitions.GetId(Goblin.Id), new Vector3Int(5, 5, Ground)) with { Seed = 1 });
        var bodyParts = build.Context.EffectServices.BodyParts;
        var partCount = bodyParts.Count(goblinId);
        var before = PartHealths(bodyParts, goblinId);
        var context = EffectContext.FromEntity(build.Context.EffectServices, goblinId, goblinId, "Test", default, build.Context.SimulationClock.CurrentFrame);

        new HealthDrain((ushort)(partCount * 2)).Apply(in context);

        var after = PartHealths(bodyParts, goblinId);
        for (var partId = 0; partId < partCount; partId++)
        {
            Assert.AreEqual(before[partId] - 2f, after[partId], 0.001f, $"Part {partId} pays its even share.");
        }

        var weakestVitalPart = float.MaxValue;
        foreach (var part in bodyParts.Parts(goblinId))
        {
            if (part.IsVital)
            {
                weakestVitalPart = MathF.Min(weakestVitalPart, part.CurrentHealth);
            }
        }

        var killingCost = (ushort)MathF.Ceiling(weakestVitalPart * partCount);
        Assert.AreEqual(EffectRefusal.NotEnoughHealth, EffectSequence.CanApply([new Effect([new HealthDrain(killingCost)])], in context));
    }

    private static List<float> PartHealths(EntityBodyParts bodyParts, int entityId)
    {
        var healths = new List<float>();
        foreach (var part in bodyParts.Parts(entityId))
        {
            healths.Add(part.CurrentHealth);
        }

        return healths;
    }

    [TestMethod]
    public void ActivationCosts_TotalEachResource()
    {
        var (context, _) = SimplePayer(currentHealth: 50);
        var action = new ActionDefinition(Guid.NewGuid(), "Test", null, "t", Color.White, [], [Effect.None],
            new DirectAction(new TargetingSpec(TargetShape.Self, Range: 0), new ActionTiming(ActionTimingCategory.Immediate)))
        {
            ActivationEffects = [new Effect([new ManaDrain(5), new HealthDrain(3)]), new Effect([new HealthDrain(2)])],
        };

        var costs = ActivationCosts.Of(context.Services, PayerEntityId, action, now: 0);

        Assert.AreEqual((5f, 5f), (costs.Mana, costs.Health));
    }
}
