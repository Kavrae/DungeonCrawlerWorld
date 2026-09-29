using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions.Effects;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Tests.Modules.StatusEffects;

[TestClass]
public sealed class StatusEffectGrantFloatingTextTests
{
    private const int SourceEntityId = 1;
    private const int TargetEntityId = 2;

    private sealed class CountingApplier(ComponentManager componentManager, StatusEffectType effectType, int maxStacks) : IStatusEffectAuraApplier
    {
        private readonly Dictionary<int, int> _stacksByEntityId = [];

        public StatusEffectType EffectType { get; } = effectType;

        public int GetCurrentStackCount(int entityId) => _stacksByEntityId.GetValueOrDefault(entityId);

        public void ApplyStack(int entityId, ActionSource source, long now)
        {
            if (StatusEffectImmunity.HasImmunity(componentManager, entityId, EffectType))
            {
                return;
            }

            _stacksByEntityId[entityId] = System.Math.Min(GetCurrentStackCount(entityId) + 1, maxStacks);
        }

        public void RemoveAllStacks(int entityId) => _stacksByEntityId.Remove(entityId);
    }

    private static (ComponentManager ComponentManager, TestFloatingText FloatingText, Game.Modules.Actions.ActionEffectContext Context) Build(int maxStacks = 10)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));
        var appliers = new StatusEffectAuraApplierRegistry();
        appliers.Register(new CountingApplier(componentManager, StatusEffectType.Burning, maxStacks));
        var floatingText = new TestFloatingText().Place(TargetEntityId, ProcessingTierLevel.Local);

        var context = TestActionEffects.Context(
            SourceEntityId: SourceEntityId,
            TargetEntityId: TargetEntityId,
            Health: componentManager.GetPackedPool<SimpleHealthComponent>(),
            EventBus: new EventBus(),
            MathUtility: new MathUtility(),
            ComponentManager: componentManager,
            EntityKeys: new EntityKeys(),
            ActivatorName: "Test",
            ActivatorTags: [],
            Now: 0,
            StatusEffectAppliers: appliers,
            FloatingTextFeed: floatingText.Feed);

        return (componentManager, floatingText, context);
    }

    [TestMethod]
    public void Apply_StacksLand_PublishesTheStacksAdded()
    {
        var (_, floatingText, context) = Build();

        new StatusEffectGrant(StatusEffectType.Burning, StackCount: 3).Apply(context);

        var published = floatingText.Published.Single();
        Assert.AreEqual(FloatingTextKind.StatusEffectStacksAdded, published.Kind);
        Assert.AreEqual(StatusEffectType.Burning, published.EffectType);
        Assert.AreEqual(3, published.Amount);
    }

    [TestMethod]
    public void Apply_SomeStacksCapped_PublishesOnlyTheStacksThatLanded()
    {
        var (_, floatingText, context) = Build(maxStacks: 2);

        new StatusEffectGrant(StatusEffectType.Burning, StackCount: 3).Apply(context);

        Assert.AreEqual(2, floatingText.Published.Single().Amount);
    }

    [TestMethod]
    public void Apply_AlreadyAtTheCap_PublishesNothing()
    {
        var (_, floatingText, context) = Build(maxStacks: 2);
        new StatusEffectGrant(StatusEffectType.Burning, StackCount: 2).Apply(context);
        floatingText.Published.Clear();

        new StatusEffectGrant(StatusEffectType.Burning, StackCount: 1).Apply(context);

        Assert.IsEmpty(floatingText.Published);
    }

    [TestMethod]
    public void Apply_ImmuneTarget_PublishesOneImmune()
    {
        var (componentManager, floatingText, context) = Build();
        componentManager.GetMultiPool<StatusEffectImmunityComponent>().Add(TargetEntityId, new StatusEffectImmunityComponent(StatusEffectType.Burning, uint.MaxValue));

        new StatusEffectGrant(StatusEffectType.Burning, StackCount: 3).Apply(context);

        var published = floatingText.Published.Single();
        Assert.AreEqual(FloatingTextKind.Immune, published.Kind);
        Assert.AreEqual(StatusEffectType.Burning, published.EffectType);
    }
}
