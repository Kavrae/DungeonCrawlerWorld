using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class ActionCooldownSystemTests
{
    private const int EntityId = 1;
    private static readonly Guid FirstActionId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondActionId = new("22222222-2222-2222-2222-222222222222");

    private static (ActionCooldownSystem System, ComponentManager ComponentManager) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterMultiPool<ActionInstanceComponent>();
        componentManager.RegisterDirectPool<ProcessingTierComponent>(static (ref existing, incoming) => existing = incoming);

        var system = new ActionCooldownSystem(componentManager.GetMultiPool<ActionInstanceComponent>(), componentManager.GetDirectPool<ProcessingTierComponent>(), new ProcessingTierEvents());

        return (system, componentManager);
    }

    private static (ActionCooldownSystem System, ComponentManager ComponentManager, DirectComponentPool<ProcessingTierComponent> Tiers) BuildWithProcessingTiers()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterMultiPool<ActionInstanceComponent>();
        componentManager.RegisterDirectPool<ProcessingTierComponent>(static (ref existing, incoming) => existing = incoming);

        var tiers = componentManager.GetDirectPool<ProcessingTierComponent>();
        var system = new ActionCooldownSystem(componentManager.GetMultiPool<ActionInstanceComponent>(), tiers, new ProcessingTierEvents());

        return (system, componentManager, tiers);
    }

    private static ushort? CooldownOf(ComponentManager componentManager, int entityId, Guid actionId)
    {
        var instances = componentManager.GetMultiPool<ActionInstanceComponent>();
        for (var i = instances.GetFirstDenseIndex(entityId); i != -1; i = instances.GetNextDenseIndex(i))
        {
            var instance = instances.GetReadonlyByDenseIndex(i);
            if (instance.ActionId == actionId)
            {
                return instance.CooldownFramesRemaining;
            }
        }

        return null;
    }

    // Entity 1, Local-tiered (no ProcessingTierComponent, base StripeCount 10 * divisor 1 = 10), lands in bucket 1 -- due only when FrameCount % 10 == 1. stripeIndex no longer drives iteration (TieredEntityStripeSet derives "due" purely from FrameCount), so it's passed as 0 throughout.
    [TestMethod]
    public void CooldownTicksDownByStripeCountPerVisit()
    {
        var (system, componentManager) = Build();
        componentManager.Merge(EntityId, new ActionInstanceComponent(FirstActionId, overrideDefinition: null, cooldownFramesRemaining: 25));

        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual((ushort?)15, CooldownOf(componentManager, EntityId, FirstActionId));
    }

    [TestMethod]
    public void CooldownFlooredAtZero_NeverGoesNegative()
    {
        var (system, componentManager) = Build();
        componentManager.Merge(EntityId, new ActionInstanceComponent(FirstActionId, overrideDefinition: null, cooldownFramesRemaining: 5));

        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual((ushort?)0, CooldownOf(componentManager, EntityId, FirstActionId));
    }

    [TestMethod]
    public void MultipleActionInstancesOnSameEntity_EachTickedIndependently()
    {
        var (system, componentManager) = Build();
        componentManager.Merge(EntityId, new ActionInstanceComponent(FirstActionId, overrideDefinition: null, cooldownFramesRemaining: 25));
        componentManager.Merge(EntityId, new ActionInstanceComponent(SecondActionId, overrideDefinition: null, cooldownFramesRemaining: 0));

        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual((ushort?)15, CooldownOf(componentManager, EntityId, FirstActionId));
        Assert.AreEqual((ushort?)0, CooldownOf(componentManager, EntityId, SecondActionId));
    }

    [TestMethod]
    public void EntityNotOnItsDueFrame_IsNotTickedThisCall()
    {
        var (system, componentManager) = Build();
        componentManager.Merge(EntityId, new ActionInstanceComponent(FirstActionId, overrideDefinition: null, cooldownFramesRemaining: 25));

        // FrameCount 0 % 10 == 0, not entity 1's due bucket (1).
        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);

        Assert.AreEqual((ushort?)25, CooldownOf(componentManager, EntityId, FirstActionId));
    }

    /// <summary>
    /// A Neighborhood-tiered entity (StripeCount 10 * divisor 2 = 20) lands in bucket
    /// entityId % 20 -- for entity 1, that's bucket 1, due only when FrameCount % 20 == 1. The
    /// tier must be seeded into the pool before ActionInstanceComponent is merged, since
    /// TieredEntityStripeSet reads an entity's current tier at membership-add time (fired by
    /// the EntityAdded event Merge raises).
    /// </summary>
    [TestMethod]
    public void Update_ThrottledEntity_OffCycle_DoesNotTick()
    {
        var (system, componentManager, tiers) = BuildWithProcessingTiers();
        tiers.Add(EntityId, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        componentManager.Merge(EntityId, new ActionInstanceComponent(FirstActionId, overrideDefinition: null, cooldownFramesRemaining: 25));

        system.Update(new EngineTime(default, default, false, FrameCount: 0), (byte)(EntityId % 10));

        Assert.AreEqual((ushort?)25, CooldownOf(componentManager, EntityId, FirstActionId));
    }

    [TestMethod]
    public void Update_ThrottledEntity_OnEligibleCycle_Ticks()
    {
        var (system, componentManager, tiers) = BuildWithProcessingTiers();
        tiers.Add(EntityId, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        componentManager.Merge(EntityId, new ActionInstanceComponent(FirstActionId, overrideDefinition: null, cooldownFramesRemaining: 25));

        system.Update(new EngineTime(default, default, false, FrameCount: 1), (byte)(EntityId % 10));

        Assert.AreEqual((ushort?)15, CooldownOf(componentManager, EntityId, FirstActionId));
    }
}
