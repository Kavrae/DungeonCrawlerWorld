using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.Events;
using Engine.Math;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Health.Components;
using Game.Modules.Mana.Components;

namespace Tests.Effects;

/// <summary>A list of effects is asked as a whole: each entry against what the ones before it left, with the first refusal's reason.</summary>
[TestClass]
public sealed class EffectSequenceCanApplyTests
{
    private const int CasterEntityId = 1;
    private const int ManalessEntityId = 2;

    private static (EffectContext Context, Func<float> ManaLeft) Caster(float currentMana)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(16, 16));
        var mana = componentManager.GetPackedPool<ManaComponent>();
        mana.Add(CasterEntityId, new ManaComponent(currentMana, 100));
        var context = TestActionEffects.Context(CasterEntityId, CasterEntityId, EmptyPools.Packed<SimpleHealthComponent>(), new EventBus(), new MathUtility(new Random(1)), componentManager, new EntityKeys(), "Test", default, Now: 0, Mana: mana);
        return (context, () => mana.GetReadonly(CasterEntityId).CurrentMana);
    }

    [TestMethod]
    public void TwoDrainsInOneEffect_EachAffordableAlone_AreRefusedTogether()
    {
        var (context, _) = Caster(currentMana: 15);

        Assert.AreEqual(EffectRefusal.NotEnoughMana, EffectSequence.CanApply([new Effect([new ManaDrain(10), new ManaDrain(10)])], in context));
    }

    [TestMethod]
    public void DrainsAcrossEffects_ExactlyAffordableTogether_AreNotRefused()
    {
        var (context, _) = Caster(currentMana: 15);

        Assert.AreEqual(EffectRefusal.None, EffectSequence.CanApply([new Effect([new ManaDrain(10)]), new Effect([new ManaDrain(5)])], in context));
    }

    [TestMethod]
    public void DrainFromATargetWithNoManaPool_IsRefusedAsHavingNone()
    {
        var (context, _) = Caster(currentMana: 15);

        Assert.AreEqual(EffectRefusal.NoManaPool, EffectSequence.CanApply([new Effect([new ManaDrain(1)])], context with { TargetEntityId = ManalessEntityId }));
    }

    [TestMethod]
    public void Asking_TakesNothing()
    {
        var (context, manaLeft) = Caster(currentMana: 15);

        EffectSequence.CanApply([new Effect([new ManaDrain(10)])], in context);

        Assert.AreEqual(15f, manaLeft());
    }

    [TestMethod]
    public void NoEffects_AreNeverRefused()
    {
        var (context, _) = Caster(currentMana: 0);

        Assert.AreEqual(EffectRefusal.None, EffectSequence.CanApply([], in context));
    }
}
