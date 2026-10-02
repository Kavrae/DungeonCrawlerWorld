using Engine.ECS.Components;
using Game.Effects;
using Game.Effects.Entries;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Engine.Utilities;
using Game.Modules.Actions;
using Game.Modules.Health.Components;
using Game.Modules.Inventory.Definitions;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Tests.Effects.Entries;

[TestClass]
public sealed class StatusEffectRemovalTests
{
    private const int SourceEntityId = 1;
    private const int TargetEntityId = 2;

    private sealed record Setup(ComponentManager ComponentManager, StatusEffectApplierRegistry Appliers, EntityKeys EntityKeys)
    {
        public EffectContext Context(long now) => TestActionEffects.Context(
            SourceEntityId: SourceEntityId,
            TargetEntityId: TargetEntityId,
            Health: ComponentManager.GetPackedPool<SimpleHealthComponent>(),
            EventBus: new EventBus(),
            MathUtility: new MathUtility(),
            ComponentManager: ComponentManager,
            EntityKeys: EntityKeys,
            ActivatorName: "Test",
            ActivatorTags: [], Now: now,
            StatModifiers: ComponentManager.GetMultiPool<StatModifierComponent>(),
            StatusEffectAppliers: Appliers);

        public void Poison(int stacks)
        {
            for (var stack = 0; stack < stacks; stack++)
            {
                PoisonEffects.ApplyStack(ComponentManager, EntityKeys, TargetEntityId, ActionSource.Admin, durationInTicks: 10, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);
            }
        }

        public int PoisonStacks => ComponentManager.GetPackedPool<PoisonTimerComponent>().TryGetReadonly(TargetEntityId, out var timer) ? timer.StackCount : 0;
    }

    private static Setup Build()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));
        var entityKeys = new EntityKeys();
        var appliers = new StatusEffectApplierRegistry();
        appliers.Register(new TimerBasedStatusEffectApplier<PoisonTimerComponent>(StatusEffectType.Poison, componentManager.GetPackedPool<PoisonTimerComponent>(), PoisonEffects.MaxStacks,
            (entityId, count, source, now, announcesRefusal) => PoisonEffects.ApplyStacks(componentManager, entityKeys, entityId, count, source, 10, now, new EventBus(), TestPlayerQuery.NoPlayer, announcesRefusal)));
        return new Setup(componentManager, appliers, entityKeys);
    }

    [TestMethod]
    public void Apply_RemovesEveryStackOfTheType()
    {
        var setup = Build();
        setup.Poison(stacks: 4);
        Assert.AreEqual(4, setup.PoisonStacks, "Sanity check: poisoned.");

        new StatusEffectRemoval(StatusEffectType.Poison).Apply(setup.Context(now: 0));

        Assert.AreEqual(0, setup.PoisonStacks);
    }

    [TestMethod]
    public void Apply_OnATargetWithoutTheEffect_DoesNothing()
    {
        var setup = Build();

        new StatusEffectRemoval(StatusEffectType.Poison).Apply(setup.Context(now: 0));

        Assert.AreEqual(0, setup.PoisonStacks);
    }

    [TestMethod]
    public void Apply_ForATypeWithNoRegisteredApplier_IsSkipped()
    {
        var setup = Build();
        setup.Poison(stacks: 2);

        new StatusEffectRemoval(StatusEffectType.Burning).Apply(setup.Context(now: 0));

        Assert.AreEqual(2, setup.PoisonStacks);
    }

    [TestMethod]
    public void CurePoisonPotion_RemovesPoison_ThenBlocksNewPoisonForFiveMinutes()
    {
        var setup = Build();
        setup.Poison(stacks: 3);
        var context = setup.Context(now: 100);

        foreach (var effect in CurePoisonPotion.Build().Effects)
        {
            foreach (var entry in effect.Entries)
            {
                entry.Apply(context);
            }
        }

        Assert.AreEqual(0, setup.PoisonStacks);
        setup.Poison(stacks: 1);
        Assert.AreEqual(0, setup.PoisonStacks, "Immune right after drinking.");

        var immunity = setup.ComponentManager.GetMultiPool<StatusEffectImmunityComponent>();
        Assert.IsTrue(immunity.TryGetFirst(TargetEntityId, static (ref readonly StatusEffectImmunityComponent candidate) => candidate.EffectType == StatusEffectType.Poison, out var poisonImmunity));
        Assert.AreEqual(FrameDeadline.After(100, 5 * 60 * GameTiming.FramesPerSecond), poisonImmunity.ExpiresAtFrame);
    }

    [TestMethod]
    public void CurePoisonPotion_IsWorthFiftyGold()
    {
        Assert.AreEqual(50, CurePoisonPotion.Build().GoldValue);
    }
}
