using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Abilities.Components;
using Game.Modules.Abilities.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Tests.Modules.Abilities;

[TestClass]
public sealed class AbilityCooldownSystemTests
{
    private const int EntityId = 1;
    private static readonly Guid FirstAbilityId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondAbilityId = new("22222222-2222-2222-2222-222222222222");

    private static (AbilityCooldownSystem System, ComponentManager ComponentManager) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterMultiPool<AbilityInstanceComponent>();
        componentManager.RegisterDirectPool<ProcessingTierComponent>(static (ref existing, incoming) => existing = incoming);

        var system = new AbilityCooldownSystem(componentManager.GetMultiPool<AbilityInstanceComponent>(), componentManager.GetDirectPool<ProcessingTierComponent>(), new ProcessingTierEvents());

        return (system, componentManager);
    }

    private static (AbilityCooldownSystem System, ComponentManager ComponentManager, DirectComponentPool<ProcessingTierComponent> Tiers) BuildWithProcessingTiers()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterMultiPool<AbilityInstanceComponent>();
        componentManager.RegisterDirectPool<ProcessingTierComponent>(static (ref existing, incoming) => existing = incoming);

        var tiers = componentManager.GetDirectPool<ProcessingTierComponent>();
        var system = new AbilityCooldownSystem(componentManager.GetMultiPool<AbilityInstanceComponent>(), tiers, new ProcessingTierEvents());

        return (system, componentManager, tiers);
    }

    private static short CooldownOf(ComponentManager componentManager, int entityId, Guid abilityId)
    {
        var instances = componentManager.GetMultiPool<AbilityInstanceComponent>();
        for (var i = instances.GetFirstDenseIndex(entityId); i != -1; i = instances.GetNextDenseIndex(i))
        {
            var instance = instances.GetReadonlyByDenseIndex(i);
            if (instance.AbilityId == abilityId)
            {
                return instance.CooldownFramesRemaining;
            }
        }

        return -1;
    }

    // Entity 1, Local-tiered (no ProcessingTierComponent, base StripeCount 10 * divisor 1 = 10), lands in bucket 1 -- due only when FrameCount % 10 == 1. stripeIndex no longer drives iteration (TieredEntityStripeSet derives "due" purely from FrameCount), so it's passed as 0 throughout.
    [TestMethod]
    public void CooldownTicksDownByStripeCountPerVisit()
    {
        var (system, componentManager) = Build();
        componentManager.Merge(EntityId, new AbilityInstanceComponent(FirstAbilityId, damageAmount: 0, cooldownFramesRemaining: 25));

        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual(15, CooldownOf(componentManager, EntityId, FirstAbilityId));
    }

    [TestMethod]
    public void CooldownFlooredAtZero_NeverGoesNegative()
    {
        var (system, componentManager) = Build();
        componentManager.Merge(EntityId, new AbilityInstanceComponent(FirstAbilityId, damageAmount: 0, cooldownFramesRemaining: 5));

        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual(0, CooldownOf(componentManager, EntityId, FirstAbilityId));
    }

    [TestMethod]
    public void MultipleAbilityInstancesOnSameEntity_EachTickedIndependently()
    {
        var (system, componentManager) = Build();
        componentManager.Merge(EntityId, new AbilityInstanceComponent(FirstAbilityId, damageAmount: 0, cooldownFramesRemaining: 25));
        componentManager.Merge(EntityId, new AbilityInstanceComponent(SecondAbilityId, damageAmount: 0, cooldownFramesRemaining: 0));

        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual(15, CooldownOf(componentManager, EntityId, FirstAbilityId));
        Assert.AreEqual(0, CooldownOf(componentManager, EntityId, SecondAbilityId));
    }

    [TestMethod]
    public void EntityNotOnItsDueFrame_IsNotTickedThisCall()
    {
        var (system, componentManager) = Build();
        componentManager.Merge(EntityId, new AbilityInstanceComponent(FirstAbilityId, damageAmount: 0, cooldownFramesRemaining: 25));

        // FrameCount 0 % 10 == 0, not entity 1's due bucket (1).
        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);

        Assert.AreEqual(25, CooldownOf(componentManager, EntityId, FirstAbilityId));
    }

    /// <summary>
    /// A Neighborhood-tiered entity (StripeCount 10 * divisor 2 = 20) lands in bucket
    /// entityId % 20 -- for entity 1, that's bucket 1, due only when FrameCount % 20 == 1. The
    /// tier must be seeded into the pool before AbilityInstanceComponent is merged, since
    /// TieredEntityStripeSet reads an entity's current tier at membership-add time (fired by
    /// the EntityAdded event Merge raises).
    /// </summary>
    [TestMethod]
    public void Update_ThrottledEntity_OffCycle_DoesNotTick()
    {
        var (system, componentManager, tiers) = BuildWithProcessingTiers();
        tiers.Add(EntityId, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        componentManager.Merge(EntityId, new AbilityInstanceComponent(FirstAbilityId, damageAmount: 0, cooldownFramesRemaining: 25));

        system.Update(new EngineTime(default, default, false, FrameCount: 0), (byte)(EntityId % 10));

        Assert.AreEqual(25, CooldownOf(componentManager, EntityId, FirstAbilityId));
    }

    [TestMethod]
    public void Update_ThrottledEntity_OnEligibleCycle_Ticks()
    {
        var (system, componentManager, tiers) = BuildWithProcessingTiers();
        tiers.Add(EntityId, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        componentManager.Merge(EntityId, new AbilityInstanceComponent(FirstAbilityId, damageAmount: 0, cooldownFramesRemaining: 25));

        system.Update(new EngineTime(default, default, false, FrameCount: 1), (byte)(EntityId % 10));

        Assert.AreEqual(15, CooldownOf(componentManager, EntityId, FirstAbilityId));
    }
}
